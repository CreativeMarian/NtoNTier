package com.ntontier.client.util

import android.content.Context
import android.content.SharedPreferences

class Prefs private constructor(context: Context) {

    private val sp: SharedPreferences =
        context.getSharedPreferences("ntontier_prefs", Context.MODE_PRIVATE)

    companion object {
        @Volatile
        private var instance: Prefs? = null

        fun init(context: Context) {
            if (instance == null) {
                synchronized(this) {
                    if (instance == null) {
                        instance = Prefs(context.applicationContext)
                    }
                }
            }
        }

        fun get(): Prefs = instance ?: throw IllegalStateException("Prefs not initialized")
    }

    var supernodeAddress: String
        get() = sp.getString("supernode_address", "supernode.ntontier.com:7654") ?: "supernode.ntontier.com:7654"
        set(v) = sp.edit().putString("supernode_address", v).apply()

    var community: String
        get() = sp.getString("community", "ntontier") ?: "ntontier"
        set(v) = sp.edit().putString("community", v).apply()

    var virtualIp: String
        get() = sp.getString("virtual_ip", "10.10.10.2") ?: "10.10.10.2"
        set(v) = sp.edit().putString("virtual_ip", v).apply()

    var encryptKey: String
        get() = sp.getString("encrypt_key", "") ?: ""
        set(v) = sp.edit().putString("encrypt_key", v).apply()

    var subnetMask: String
        get() = sp.getString("subnet_mask", "255.255.255.0") ?: "255.255.255.0"
        set(v) = sp.edit().putString("subnet_mask", v).apply()

    var mtu: Int
        get() = sp.getInt("mtu", 1400)
        set(v) = sp.edit().putInt("mtu", v).apply()

    var autoConnect: Boolean
        get() = sp.getBoolean("auto_connect", false)
        set(v) = sp.edit().putBoolean("auto_connect", v).apply()

    var hfsDefaultPort: Int
        get() = sp.getInt("hfs_default_port", 8080)
        set(v) = sp.edit().putInt("hfs_default_port", v).apply()

    var downloadThreads: Int
        get() = sp.getInt("download_threads", 8)
        set(v) = sp.edit().putInt("download_threads", v).apply()

    var downloadPath: String
        get() = sp.getString("download_path", "") ?: ""
        set(v) = sp.edit().putString("download_path", v).apply()

    var vpnConnected: Boolean
        get() = sp.getBoolean("vpn_connected", false)
        set(v) = sp.edit().putBoolean("vpn_connected", v).apply()
}
