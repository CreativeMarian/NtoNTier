package com.ntontier.client.ui.members

import android.content.Intent
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.Toast
import androidx.fragment.app.Fragment
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.swiperefreshlayout.widget.SwipeRefreshLayout
import com.ntontier.client.databinding.FragmentMembersBinding
import com.ntontier.client.scan.Member
import com.ntontier.client.scan.MemberScanner
import com.ntontier.client.ui.browse.FileBrowseActivity
import com.ntontier.client.util.Prefs
import com.ntontier.client.vpn.N2NVpnService

class MembersFragment : Fragment(), MemberScanner.ScanListener, SwipeRefreshLayout.OnRefreshListener {

    private var _binding: FragmentMembersBinding? = null
    private val binding get() = _binding!!
    private lateinit var adapter: MemberAdapter
    private val scanner = MemberScanner()
    private val handler = Handler(Looper.getMainLooper())

    override fun onCreateView(inflater: LayoutInflater, container: ViewGroup?, savedInstanceState: Bundle?): View {
        _binding = FragmentMembersBinding.inflate(inflater, container, false)
        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        super.onViewCreated(view, savedInstanceState)
        adapter = MemberAdapter { member -> onMemberClick(member) }
        binding.recyclerView.layoutManager = LinearLayoutManager(requireContext())
        binding.recyclerView.adapter = adapter
        binding.swipeRefresh.setOnRefreshListener(this)
        binding.swipeRefresh.setColorSchemeResources(android.R.color.holo_blue_light)
        startScan()
    }

    private fun startScan() {
        if (!N2NVpnService.isRunning) {
            Toast.makeText(requireContext(), "请先连接 n2n VPN", Toast.LENGTH_SHORT).show()
            binding.tvEmpty.visibility = View.VISIBLE
            binding.tvEmpty.text = "请先连接 n2n VPN\n然后下拉刷新扫描成员"
            return
        }
        binding.swipeRefresh.isRefreshing = true
        binding.tvEmpty.visibility = View.GONE
        val vip = Prefs.get().virtualIp
        val mask = Prefs.get().subnetMask
        scanner.startScan(vip, mask)
    }

    private fun onMemberClick(member: Member) {
        if (member.hfsPort > 0) {
            val intent = Intent(requireContext(), FileBrowseActivity::class.java).apply {
                putExtra("member_ip", member.ip)
                putExtra("hfs_port", member.hfsPort)
            }
            startActivity(intent)
        } else {
            Toast.makeText(requireContext(), "该成员未开启 HFS 文件服务", Toast.LENGTH_SHORT).show()
        }
    }

    override fun onRefresh() {
        startScan()
    }

    override fun onMemberFound(member: Member) {
        handler.post {
            if (_binding != null) {
                adapter.updateMember(member)
                binding.tvEmpty.visibility = View.GONE
            }
        }
    }

    override fun onMemberUpdated(member: Member) {
        handler.post {
            if (_binding != null) {
                adapter.updateMember(member)
            }
        }
    }

    override fun onScanProgress(scanned: Int, total: Int) {
        handler.post {
            if (_binding != null) {
                binding.tvScanProgress.text = "扫描中... $scanned/$total"
            }
        }
    }

    override fun onScanComplete(found: Int) {
        handler.post {
            if (_binding != null) {
                binding.swipeRefresh.isRefreshing = false
                binding.tvScanProgress.text = "扫描完成，发现 $found 个在线成员"
                if (found == 0) {
                    binding.tvEmpty.visibility = View.VISIBLE
                    binding.tvEmpty.text = "未发现在线成员\n请确认已连接 n2n 并下拉刷新"
                }
            }
        }
    }

    override fun onResume() {
        super.onResume()
        scanner.addListener(this)
    }

    override fun onPause() {
        super.onPause()
        scanner.removeListener(this)
    }

    override fun onDestroyView() {
        handler.removeCallbacksAndMessages(null)
        scanner.stopScan()
        scanner.removeListener(this)
        super.onDestroyView()
        _binding = null
    }
}
