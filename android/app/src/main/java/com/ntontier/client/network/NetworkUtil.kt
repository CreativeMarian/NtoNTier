package com.ntontier.client.network

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.Socket

object NetworkUtil {

    suspend fun pingHost(ip: String, timeoutMs: Int = 1500): Boolean = withContext(Dispatchers.IO) {
        try {
            val addr = InetAddress.getByName(ip)
            addr.isReachable(timeoutMs)
        } catch (e: Exception) {
            false
        }
    }

    suspend fun isPortOpen(ip: String, port: Int, timeoutMs: Int = 1500): Boolean = withContext(Dispatchers.IO) {
        try {
            Socket().use { socket ->
                socket.connect(InetSocketAddress(ip, port), timeoutMs)
                socket.isConnected
            }
        } catch (e: Exception) {
            false
        }
    }

    fun getSubnet(ip: String, mask: String = "255.255.255.0"): String {
        val ipParts = ip.split(".").map { it.toIntOrNull() ?: 0 }
        val maskParts = mask.split(".").map { it.toIntOrNull() ?: 0 }
        if (ipParts.size != 4 || maskParts.size != 4) return "10.10.10.0"
        val network = ipParts.zip(maskParts) { a, b -> a and b }
        return network.joinToString(".")
    }

    fun getNetworkPrefix(mask: String): Int {
        val maskParts = mask.split(".").map { it.toIntOrNull() ?: 0 }
        var prefix = 0
        for (part in maskParts) {
            var p = part
            while (p > 0) {
                prefix += p and 1
                p = p shr 1
            }
        }
        return prefix
    }

    fun ipToLong(ip: String): Long {
        val parts = ip.split(".").map { it.toLongOrNull() ?: 0 }
        if (parts.size != 4) return 0
        return (parts[0] shl 24) or (parts[1] shl 16) or (parts[2] shl 8) or parts[3]
    }

    fun longToIp(value: Long): String {
        return "${(value shr 24) and 0xFF}.${(value shr 16) and 0xFF}.${(value shr 8) and 0xFF}.${value and 0xFF}"
    }

    fun getHostname(ip: String): String {
        return try {
            InetAddress.getByName(ip).hostName ?: ip
        } catch (e: Exception) {
            ip
        }
    }
}
