package com.ntontier.client.vpn

import android.app.Notification
import android.app.PendingIntent
import android.content.Intent
import android.net.VpnService
import android.os.Build
import android.os.ParcelFileDescriptor
import android.util.Log
import androidx.core.app.NotificationCompat
import com.ntontier.client.NtoNApplication
import com.ntontier.client.R
import com.ntontier.client.ui.MainActivity
import com.ntontier.client.util.Prefs
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import java.io.FileInputStream
import java.io.FileOutputStream
import java.net.DatagramSocket
import java.net.InetAddress
import java.nio.ByteBuffer

class N2NVpnService : VpnService() {

    interface ConnectionListener {
        fun onConnected(virtualIp: String)
        fun onDisconnected()
        fun onError(message: String)
        fun onStatusUpdate(message: String)
    }

    companion object {
        private const val TAG = "N2NVpnService"
        private const val VPN_MTU = 1400
        private const val NOTIFICATION_ID = 1001

        @Volatile
        var isRunning = false
            private set

        var connectionListener: ConnectionListener? = null
    }

    private var vpnInterface: ParcelFileDescriptor? = null
    private val serviceScope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private var tunnelJob: Job? = null
    private var n2nProcess: Process? = null
    private var socket: DatagramSocket? = null

    override fun onCreate() {
        super.onCreate()
        Log.d(TAG, "onCreate")
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        Log.d(TAG, "onStartCommand action=${intent?.action}")
        when (intent?.action) {
            "ACTION_CONNECT" -> {
                startForeground(NOTIFICATION_ID, buildNotification("正在连接 n2n..."))
                connect()
            }
            "ACTION_DISCONNECT" -> {
                disconnect()
                stopSelf()
            }
            else -> {
                if (isRunning) {
                    startForeground(NOTIFICATION_ID, buildNotification("n2n 已连接"))
                }
            }
        }
        return START_STICKY
    }

    private fun buildNotification(contentText: String): Notification {
        val pendingIntent = PendingIntent.getActivity(
            this, 0,
            Intent(this, MainActivity::class.java),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT
        )
        return NotificationCompat.Builder(this, NtoNApplication.CHANNEL_VPN)
            .setContentTitle("NtoNTier VPN")
            .setContentText(contentText)
            .setSmallIcon(android.R.drawable.ic_menu_compass)
            .setContentIntent(pendingIntent)
            .setOngoing(true)
            .setPriority(NotificationCompat.PRIORITY_LOW)
            .build()
    }

    private fun connect() {
        if (isRunning) {
            Log.w(TAG, "VPN already running")
            return
        }
        serviceScope.launch {
            try {
                connectionListener?.onStatusUpdate("正在建立 VPN 接口...")
                establishVpnInterface()
                connectionListener?.onStatusUpdate("正在启动 n2n edge...")
                startN2nEdge()
                connectionListener?.onStatusUpdate("正在启动隧道转发...")
                startTunnelLoop()
                isRunning = true
                Prefs.get().vpnConnected = true
                val vip = Prefs.get().virtualIp
                connectionListener?.onConnected(vip)
                updateNotification("n2n 已连接: $vip")
                Log.i(TAG, "VPN connected, virtual IP: $vip")
            } catch (e: Exception) {
                Log.e(TAG, "Connect failed", e)
                connectionListener?.onError("连接失败: ${e.message}")
                cleanup()
            }
        }
    }

    private fun establishVpnInterface() {
        val prefs = Prefs.get()
        val builder = Builder()
            .setSession("NtoNTier")
            .setMtu(VPN_MTU)
            .addAddress(prefs.virtualIp, 24)
            .addRoute("0.0.0.0", 0)
            .addDnsServer("8.8.8.8")
            .addDnsServer("114.114.114.114")

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            builder.setMetered(false)
        }

        vpnInterface = builder.establish()
            ?: throw IllegalStateException("VPN interface establish returned null")
        Log.d(TAG, "VPN interface established: ${prefs.virtualIp}/24")
    }

    private suspend fun startN2nEdge() {
        val prefs = Prefs.get()
        val edgeBinary = getEdgeBinaryPath()
        if (edgeBinary == null || !java.io.File(edgeBinary).exists()) {
            Log.w(TAG, "n2n edge binary not found, running in framework-only mode")
            connectionListener?.onStatusUpdate("未找到 n2n edge 二进制，运行于框架模式")
            return
        }
        try {
            val cmd = mutableListOf(
                edgeBinary,
                "-c", prefs.community,
                "-l", prefs.supernodeAddress,
                "-a", prefs.virtualIp,
                "-s", prefs.subnetMask,
                "-m", "random",
                "-d", "ntontier0"
            )
            if (prefs.encryptKey.isNotEmpty()) {
                cmd.add("-k")
                cmd.add(prefs.encryptKey)
            }
            Log.d(TAG, "Starting n2n edge: ${cmd.joinToString(" ")}")
            n2nProcess = ProcessBuilder(cmd)
                .redirectErrorStream(true)
                .start()
            // Read process output
            serviceScope.launch {
                n2nProcess?.inputStream?.bufferedReader()?.use { reader ->
                    reader.lineSequence().forEach { line ->
                        Log.d(TAG, "edge: $line")
                    }
                }
            }
            delay(1500)
            if (n2nProcess?.isAlive == false) {
                Log.w(TAG, "n2n edge exited early")
            }
        } catch (e: Exception) {
            Log.e(TAG, "Failed to start n2n edge", e)
            throw e
        }
    }

    private fun getEdgeBinaryPath(): String? {
        val abi = Build.SUPPORTED_ABIS.firstOrNull() ?: return null
        val dir = applicationInfo.nativeLibraryDir
        val candidates = listOf(
            "$dir/libedge.so",
            "$dir/libn2n_edge.so",
            filesDir.absolutePath + "/edge_$abi"
        )
        return candidates.firstOrNull { java.io.File(it).exists() }
    }

    private fun startTunnelLoop() {
        val pfd = vpnInterface ?: return
        tunnelJob = serviceScope.launch {
            val input = FileInputStream(pfd.fileDescriptor)
            val buffer = ByteBuffer.allocate(VPN_MTU)
            Log.d(TAG, "Tunnel loop started")
            while (isActive) {
                try {
                    buffer.clear()
                    val length = input.read(buffer.array())
                    if (length > 0) {
                        buffer.limit(length)
                        handleOutgoingPacket(buffer)
                    } else {
                        delay(10)
                    }
                } catch (e: Exception) {
                    if (isActive) {
                        Log.w(TAG, "Tunnel read error", e)
                        delay(100)
                    }
                }
            }
            Log.d(TAG, "Tunnel loop ended")
        }
    }

    private fun handleOutgoingPacket(buffer: ByteBuffer) {
        // In full implementation, packets are forwarded to n2n edge via TUN interface.
        // Framework mode: packets are consumed to keep VPN interface active.
        // The actual n2n edge process handles routing when binary is present.
        buffer.clear()
    }

    private fun disconnect() {
        Log.d(TAG, "disconnect")
        isRunning = false
        Prefs.get().vpnConnected = false
        connectionListener?.onDisconnected()
        cleanup()
    }

    private fun cleanup() {
        try {
            tunnelJob?.cancel()
            tunnelJob = null
        } catch (e: Exception) { Log.w(TAG, "cancel tunnelJob", e) }
        try {
            n2nProcess?.destroy()
            n2nProcess = null
        } catch (e: Exception) { Log.w(TAG, "destroy n2nProcess", e) }
        try {
            socket?.close()
            socket = null
        } catch (e: Exception) { Log.w(TAG, "close socket", e) }
        try {
            vpnInterface?.close()
            vpnInterface = null
        } catch (e: Exception) { Log.w(TAG, "close vpnInterface", e) }
    }

    private fun updateNotification(text: String) {
        val nm = getSystemService(android.app.NotificationManager::class.java)
        nm.notify(NOTIFICATION_ID, buildNotification(text))
    }

    override fun onDestroy() {
        Log.d(TAG, "onDestroy")
        disconnect()
        serviceScope.cancel()
        super.onDestroy()
    }

    override fun onRevoke() {
        Log.d(TAG, "onRevoke")
        disconnect()
        super.onRevoke()
    }
}
