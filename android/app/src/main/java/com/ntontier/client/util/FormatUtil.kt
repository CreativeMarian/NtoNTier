package com.ntontier.client.util

import java.text.DecimalFormat
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

object FormatUtil {

    private val speedFormat = DecimalFormat("#.##")
    private val sizeFormat = DecimalFormat("#.##")
    private val dateFormat = SimpleDateFormat("yyyy-MM-dd HH:mm:ss", Locale.getDefault())

    fun formatSpeed(bytesPerSecond: Long): String {
        return when {
            bytesPerSecond >= 1024 * 1024 * 1024 -> "${speedFormat.format(bytesPerSecond / (1024.0 * 1024 * 1024))} GB/s"
            bytesPerSecond >= 1024 * 1024 -> "${speedFormat.format(bytesPerSecond / (1024.0 * 1024))} MB/s"
            bytesPerSecond >= 1024 -> "${speedFormat.format(bytesPerSecond / 1024.0)} KB/s"
            else -> "$bytesPerSecond B/s"
        }
    }

    fun formatSize(bytes: Long): String {
        return when {
            bytes >= 1024L * 1024 * 1024 * 1024 -> "${sizeFormat.format(bytes / (1024.0 * 1024 * 1024 * 1024))} TB"
            bytes >= 1024L * 1024 * 1024 -> "${sizeFormat.format(bytes / (1024.0 * 1024 * 1024))} GB"
            bytes >= 1024 * 1024 -> "${sizeFormat.format(bytes / (1024.0 * 1024))} MB"
            bytes >= 1024 -> "${sizeFormat.format(bytes / 1024.0)} KB"
            else -> "$bytes B"
        }
    }

    fun formatTime(seconds: Long): String {
        if (seconds <= 0) return "--:--"
        val h = seconds / 3600
        val m = (seconds % 3600) / 60
        val s = seconds % 60
        return if (h > 0) String.format("%d:%02d:%02d", h, m, s)
        else String.format("%02d:%02d", m, s)
    }

    fun formatDate(timestamp: Long): String {
        return dateFormat.format(Date(timestamp))
    }

    fun formatPercent(downloaded: Long, total: Long): Int {
        if (total <= 0) return 0
        return ((downloaded * 100) / total).toInt().coerceIn(0, 100)
    }
}
