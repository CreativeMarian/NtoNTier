package com.ntontier.client.ui.connect

import android.content.Intent
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.Toast
import androidx.fragment.app.Fragment
import com.ntontier.client.databinding.FragmentConnectBinding
import com.ntontier.client.ui.MainActivity
import com.ntontier.client.util.Prefs
import com.ntontier.client.vpn.N2NVpnService

class ConnectFragment : Fragment(), N2NVpnService.ConnectionListener {

    private var _binding: FragmentConnectBinding? = null
    private val binding get() = _binding!!
    private val handler = Handler(Looper.getMainLooper())

    override fun onCreateView(inflater: LayoutInflater, container: ViewGroup?, savedInstanceState: Bundle?): View {
        _binding = FragmentConnectBinding.inflate(inflater, container, false)
        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        super.onViewCreated(view, savedInstanceState)
        loadConfig()
        setupListeners()
        updateConnectionState()
    }

    private fun loadConfig() {
        val prefs = Prefs.get()
        binding.etSupernode.setText(prefs.supernodeAddress)
        binding.etCommunity.setText(prefs.community)
        binding.etVirtualIp.setText(prefs.virtualIp)
        binding.etEncryptKey.setText(prefs.encryptKey)
        binding.etSubnetMask.setText(prefs.subnetMask)
        binding.etMtu.setText(prefs.mtu.toString())
        binding.cbAutoConnect.isChecked = prefs.autoConnect
    }

    private fun saveConfig() {
        val prefs = Prefs.get()
        prefs.supernodeAddress = binding.etSupernode.text.toString().trim()
        prefs.community = binding.etCommunity.text.toString().trim()
        prefs.virtualIp = binding.etVirtualIp.text.toString().trim()
        prefs.encryptKey = binding.etEncryptKey.text.toString().trim()
        prefs.subnetMask = binding.etSubnetMask.text.toString().trim()
        prefs.mtu = binding.etMtu.text.toString().toIntOrNull() ?: 1400
        prefs.autoConnect = binding.cbAutoConnect.isChecked
    }

    private fun setupListeners() {
        binding.btnConnect.setOnClickListener {
            if (N2NVpnService.isRunning) {
                (activity as? MainActivity)?.disconnectVpn()
            } else {
                saveConfig()
                if (validateConfig()) {
                    (activity as? MainActivity)?.checkVpnPermissionAndConnect()
                }
            }
        }
    }

    private fun validateConfig(): Boolean {
        if (binding.etSupernode.text.isBlank()) {
            Toast.makeText(requireContext(), "请输入 Supernode 地址", Toast.LENGTH_SHORT).show()
            return false
        }
        if (binding.etCommunity.text.isBlank()) {
            Toast.makeText(requireContext(), "请输入社区名", Toast.LENGTH_SHORT).show()
            return false
        }
        if (binding.etVirtualIp.text.isBlank()) {
            Toast.makeText(requireContext(), "请输入虚拟 IP", Toast.LENGTH_SHORT).show()
            return false
        }
        return true
    }

    private fun updateConnectionState() {
        if (N2NVpnService.isRunning) {
            binding.btnConnect.text = "断开连接"
            binding.tvStatus.text = "已连接"
            binding.tvStatus.setTextColor(requireContext().getColor(android.R.color.holo_green_light))
            binding.tvVirtualIp.text = "虚拟 IP: ${Prefs.get().virtualIp}"
        } else {
            binding.btnConnect.text = "连接"
            binding.tvStatus.text = "未连接"
            binding.tvStatus.setTextColor(requireContext().getColor(android.R.color.darker_gray))
            binding.tvVirtualIp.text = ""
        }
    }

    override fun onResume() {
        super.onResume()
        N2NVpnService.connectionListener = this
        updateConnectionState()
    }

    override fun onPause() {
        super.onPause()
        if (N2NVpnService.connectionListener === this) {
            N2NVpnService.connectionListener = null
        }
    }

    override fun onConnected(virtualIp: String) {
        handler.post {
            updateConnectionState()
            Toast.makeText(requireContext(), "n2n 已连接: $virtualIp", Toast.LENGTH_SHORT).show()
        }
    }

    override fun onDisconnected() {
        handler.post {
            updateConnectionState()
            Toast.makeText(requireContext(), "n2n 已断开", Toast.LENGTH_SHORT).show()
        }
    }

    override fun onError(message: String) {
        handler.post {
            updateConnectionState()
            Toast.makeText(requireContext(), message, Toast.LENGTH_LONG).show()
        }
    }

    override fun onStatusUpdate(message: String) {
        handler.post {
            binding.tvStatus.text = message
        }
    }

    override fun onDestroyView() {
        handler.removeCallbacksAndMessages(null)
        super.onDestroyView()
        _binding = null
    }
}
