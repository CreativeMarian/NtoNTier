package com.ntontier.client.ui.guide

import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.fragment.app.Fragment
import com.ntontier.client.databinding.FragmentGuideBinding

class GuideFragment : Fragment() {

    private var _binding: FragmentGuideBinding? = null
    private val binding get() = _binding!!

    override fun onCreateView(inflater: LayoutInflater, container: ViewGroup?, savedInstanceState: Bundle?): View {
        _binding = FragmentGuideBinding.inflate(inflater, container, false)
        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        super.onViewCreated(view, savedInstanceState)
        binding.tvGuide.text = buildString {
            append("NtoNTier 安卓端使用指南\n\n")
            append("1. 联机功能\n")
            append("   - 在「联机功能」页面配置 Supernode 地址、社区名、虚拟 IP、加密 Key\n")
            append("   - 点击「连接」按钮，授权 VPN 权限后自动建立 n2n 隧道\n")
            append("   - 连接成功后，同社区成员可通过虚拟 IP 互相访问\n\n")
            append("2. 在线成员\n")
            append("   - 在「在线成员」页面下拉刷新，自动扫描同社区在线成员\n")
            append("   - 显示成员 IP、主机名、在线状态、HFS 端口\n")
            append("   - 点击成员可直接浏览其共享文件\n\n")
            append("3. 文件浏览\n")
            append("   - 从成员列表点击进入，或在「文件浏览」页面手动输入 IP 和端口\n")
            append("   - 默认 HFS 端口 8080，自动探测 8000-8099/8888/9000\n")
            append("   - 支持文件夹导航，点击文件下载\n\n")
            append("4. 文件下载\n")
            append("   - 采用分片多线程下载（默认 8 线程）\n")
            append("   - 在「下载管理」页面查看进度、速度，支持暂停/继续/取消\n")
            append("   - 下载文件保存在应用专属目录\n\n")
            append("5. 网络测试\n")
            append("   - 支持 Ping 测试和常用端口扫描\n")
            append("   - 用于诊断网络连通性\n\n")
            append("6. 文件共享\n")
            append("   - 当前版本暂不支持安卓端作为 HFS 服务端\n")
            append("   - 仅支持浏览和下载 Windows 端共享的文件\n\n")
            append("注意事项：\n")
            append("   - n2n edge 二进制需交叉编译后放入 jniLibs 目录\n")
            append("   - 未集成二进制时，VPN 框架仍可运行，但无法实际建立 n2n 隧道\n")
            append("   - 建议在 WiFi 环境下使用以获得最佳体验\n")
        }
    }

    override fun onDestroyView() {
        super.onDestroyView()
        _binding = null
    }
}
