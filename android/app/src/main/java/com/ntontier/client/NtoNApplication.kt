package com.ntontier.client

import android.app.Application
import android.app.NotificationChannel
import android.app.NotificationManager
import android.os.Build
import com.ntontier.client.util.Prefs

class NtoNApplication : Application() {

    companion object {
        const val CHANNEL_VPN = "vpn_channel"
        const val CHANNEL_DOWNLOAD = "download_channel"
        lateinit var instance: NtoNApplication
            private set
    }

    override fun onCreate() {
        super.onCreate()
        instance = this
        Prefs.init(this)
        createNotificationChannels()
    }

    private fun createNotificationChannels() {
        val nm = getSystemService(NotificationManager::class.java)
        val vpnChannel = NotificationChannel(
            CHANNEL_VPN,
            "N2N VPN 连接",
            NotificationManager.IMPORTANCE_LOW
        ).apply {
            description = "显示 n2n VPN 连接状态"
            setShowBadge(false)
        }
        val downloadChannel = NotificationChannel(
            CHANNEL_DOWNLOAD,
            "文件下载",
            NotificationManager.IMPORTANCE_LOW
        ).apply {
            description = "显示文件下载进度"
            setShowBadge(false)
        }
        nm.createNotificationChannel(vpnChannel)
        nm.createNotificationChannel(downloadChannel)
    }
}
