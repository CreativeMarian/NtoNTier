package com.ntontier.client.scan

import com.ntontier.client.network.NetworkUtil
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch

data class Member(
    val ip: String,
    var hostname: String = "",
    var isOnline: Boolean = false,
    var hfsPort: Int = -1,
    var lastSeen: Long = 0
)

class MemberScanner {

    companion object {
        private const val SCAN_CONCURRENCY = 32
        private val HFS_PORTS = listOf(8080, 8000, 8001, 8002, 8003, 8004, 8005, 8006, 8007, 8008, 8009, 8010, 8888, 9000)
    }

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private var scanJob: Job? = null
    private val members = mutableMapOf<String, Member>()
    private val listeners = mutableListOf<ScanListener>()

    interface ScanListener {
        fun onMemberFound(member: Member)
        fun onMemberUpdated(member: Member)
        fun onScanProgress(scanned: Int, total: Int)
        fun onScanComplete(found: Int)
    }

    fun addListener(listener: ScanListener) {
        if (!listeners.contains(listener)) listeners.add(listener)
    }

    fun removeListener(listener: ScanListener) {
        listeners.remove(listener)
    }

    fun getMembers(): List<Member> = members.values.filter { it.isOnline }.sortedBy { it.ip }

    fun isScanning(): Boolean = scanJob?.isActive == true

    fun startScan(networkIp: String, mask: String = "255.255.255.0") {
        if (scanJob?.isActive == true) {
            scanJob?.cancel()
        }
        scanJob = scope.launch {
            val subnet = NetworkUtil.getSubnet(networkIp, mask)
            val prefix = NetworkUtil.getNetworkPrefix(mask)
            val hostCount = if (prefix >= 24) 254 else (1 shl (32 - prefix)) - 2
            val networkLong = NetworkUtil.ipToLong(subnet)

            val ips = (1..hostCount.coerceAtMost(254)).map { offset ->
                NetworkUtil.longToIp(networkLong + offset)
            }.filter { it != networkIp }

            var scanned = 0
            val total = ips.size

            ips.chunked(SCAN_CONCURRENCY).forEach { chunk ->
                if (!isActive) return@launch
                coroutineScope {
                    chunk.map { ip ->
                        async {
                            val online = NetworkUtil.pingHost(ip, 1200)
                            if (online) {
                                val member = members[ip] ?: Member(ip = ip)
                                member.isOnline = true
                                member.lastSeen = System.currentTimeMillis()
                                if (member.hostname.isEmpty()) {
                                    member.hostname = NetworkUtil.getHostname(ip)
                                }
                                // Probe HFS port
                                member.hfsPort = probeHfsPort(ip)
                                members[ip] = member
                                listeners.forEach { it.onMemberFound(member) }
                            } else {
                                members[ip]?.let {
                                    it.isOnline = false
                                    listeners.forEach { l -> l.onMemberUpdated(it) }
                                }
                            }
                        }
                    }.awaitAll()
                }
                scanned += chunk.size
                listeners.forEach { it.onScanProgress(scanned, total) }
            }
            val found = members.values.count { it.isOnline }
            listeners.forEach { it.onScanComplete(found) }
        }
    }

    private suspend fun probeHfsPort(ip: String): Int {
        for (port in HFS_PORTS) {
            if (NetworkUtil.isPortOpen(ip, port, 800)) {
                return port
            }
        }
        return -1
    }

    fun stopScan() {
        scanJob?.cancel()
        scanJob = null
    }

    fun refreshMember(ip: String) {
        scope.launch {
            val online = NetworkUtil.pingHost(ip, 1500)
            val member = members[ip] ?: Member(ip = ip)
            member.isOnline = online
            if (online) {
                member.lastSeen = System.currentTimeMillis()
                member.hfsPort = probeHfsPort(ip)
                if (member.hostname.isEmpty()) {
                    member.hostname = NetworkUtil.getHostname(ip)
                }
            }
            members[ip] = member
            listeners.forEach { it.onMemberUpdated(member) }
        }
    }

    fun clear() {
        stopScan()
        members.clear()
    }
}
