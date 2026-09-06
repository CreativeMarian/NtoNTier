package com.ntontier.client.vpn

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.util.Log
import com.ntontier.client.util.Prefs

class BootReceiver : BroadcastReceiver() {
    companion object {
        private const val TAG = "BootReceiver"
    }

    override fun onReceive(context: Context, intent: Intent) {
        if (intent.action == Intent.ACTION_BOOT_COMPLETED) {
            Log.d(TAG, "Boot completed, autoConnect=${Prefs.get().autoConnect}")
            if (Prefs.get().autoConnect) {
                val vpnIntent = Intent(context, N2NVpnService::class.java).apply {
                    action = "ACTION_CONNECT"
                }
                try {
                    context.startForegroundService(vpnIntent)
                } catch (e: Exception) {
                    Log.e(TAG, "Failed to start VPN on boot", e)
                }
            }
        }
    }
}
