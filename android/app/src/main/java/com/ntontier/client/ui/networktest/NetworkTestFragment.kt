package com.ntontier.client.ui.networktest

import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.Toast
import androidx.fragment.app.Fragment
import com.ntontier.client.databinding.FragmentNetworkTestBinding
import com.ntontier.client.network.NetworkUtil
import com.ntontier.client.util.Prefs
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.net.InetAddress

class NetworkTestFragment : Fragment() {

    private var _binding: FragmentNetworkTestBinding? = null
    private val binding get() = _binding!!
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val handler = Handler(Looper.getMainLooper())
    private var testJob: Job? = null

    override fun onCreateView(inflater: LayoutInflater, container: ViewGroup?, savedInstanceState: Bundle?): View {
        _binding = FragmentNetworkTestBinding.inflate(inflater, container, false)
        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        super.onViewCreated(view, savedInstanceState)
        binding.etTargetIp.setText(Prefs.get().virtualIp)
        binding.btnTest.setOnClickListener { runTest() }
    }

    private fun runTest() {
        val target = binding.etTargetIp.text.toString().trim()
        if (target.isEmpty()) {
            Toast.makeText(requireContext(), "请输入目标 IP", Toast.LENGTH_SHORT).show()
            return
        }
        testJob?.cancel()
        binding.tvResult.text = "正在测试..."
        binding.btnTest.isEnabled = false

        testJob = scope.launch {
            val results = mutableListOf<String>()
            results.add("目标: $target")
            results.add("")

            // DNS resolution
            val dnsStart = System.currentTimeMillis()
            val resolved = try {
                InetAddress.getByName(target).hostAddress
            } catch (e: Exception) {
                null
            }
            val dnsTime = System.currentTimeMillis() - dnsStart
            results.add("DNS 解析: ${resolved ?: "失败"} (${dnsTime}ms)")

            // Ping test
            results.add("")
            results.add("=== Ping 测试 ===")
            var successCount = 0
            var totalTime = 0L
            var minTime = Long.MAX_VALUE
            var maxTime = 0L
            for (i in 1..4) {
                val start = System.currentTimeMillis()
                val reachable = NetworkUtil.pingHost(target, 2000)
                val time = System.currentTimeMillis() - start
                if (reachable) {
                    successCount++
                    totalTime += time
                    minTime = minOf(minTime, time)
                    maxTime = maxOf(maxTime, time)
                    results.add("Ping $i: 可达 (${time}ms)")
                } else {
                    results.add("Ping $i: 超时")
                }
            }
            if (successCount > 0) {
                val avg = totalTime / successCount
                results.add("")
                results.add("统计: $successCount/4 成功, 平均 ${avg}ms, 最小 ${minTime}ms, 最大 ${maxTime}ms")
            }

            // Port scan
            results.add("")
            results.add("=== 端口扫描 ===")
            val ports = listOf(80, 8080, 8000, 8888, 9000, 22, 445, 3389)
            for (port in ports) {
                val open = NetworkUtil.isPortOpen(target, port, 1000)
                if (open) {
                    results.add("端口 $port: 开放")
                }
            }
            if (results.none { it.contains("开放") && it.contains("端口") }) {
                results.add("未发现常用开放端口")
            }

            withContext(Dispatchers.Main) {
                binding.tvResult.text = results.joinToString("\n")
                binding.btnTest.isEnabled = true
            }
        }
    }

    override fun onDestroyView() {
        super.onDestroyView()
        testJob?.cancel()
        scope.cancel()
        _binding = null
    }
}
