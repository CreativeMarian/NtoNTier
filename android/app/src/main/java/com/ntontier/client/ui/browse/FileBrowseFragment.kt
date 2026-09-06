package com.ntontier.client.ui.browse

import android.content.Intent
import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.Toast
import androidx.fragment.app.Fragment
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.LinearLayoutManager
import com.ntontier.client.databinding.FragmentFileBrowseBinding
import com.ntontier.client.download.DownloadService
import com.ntontier.client.hfs.HfsClient
import com.ntontier.client.hfs.HfsFile
import com.ntontier.client.util.Prefs
import kotlinx.coroutines.launch

class FileBrowseFragment : Fragment() {

    private var _binding: FragmentFileBrowseBinding? = null
    private val binding get() = _binding!!
    private lateinit var adapter: HfsFileAdapter
    private val hfsClient = HfsClient()
    private var currentPath = "/"
    private var currentBaseUrl = ""

    override fun onCreateView(inflater: LayoutInflater, container: ViewGroup?, savedInstanceState: Bundle?): View {
        _binding = FragmentFileBrowseBinding.inflate(inflater, container, false)
        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        super.onViewCreated(view, savedInstanceState)
        adapter = HfsFileAdapter(
            onFileClick = { file -> onFileClick(file) },
            onDownloadClick = { file -> downloadFile(file) }
        )
        binding.recyclerView.layoutManager = LinearLayoutManager(requireContext())
        binding.recyclerView.adapter = adapter

        binding.btnBrowse.setOnClickListener {
            val ip = binding.etIp.text.toString().trim()
            val port = binding.etPort.text.toString().toIntOrNull() ?: Prefs.get().hfsDefaultPort
            if (ip.isEmpty()) {
                Toast.makeText(requireContext(), "请输入成员 IP", Toast.LENGTH_SHORT).show()
                return@setOnClickListener
            }
            currentBaseUrl = "http://$ip:$port"
            currentPath = "/"
            loadDirectory()
        }

        binding.btnBack.setOnClickListener {
            if (currentPath != "/") {
                currentPath = currentPath.substringBeforeLast("/", "/")
                if (currentPath.isEmpty()) currentPath = "/"
                loadDirectory()
            }
        }
    }

    private fun onFileClick(file: HfsFile) {
        if (file.isDirectory) {
            currentPath = file.url.substringAfter(currentBaseUrl, "/")
            if (currentPath.isEmpty()) currentPath = "/"
            loadDirectory()
        } else {
            downloadFile(file)
        }
    }

    private fun downloadFile(file: HfsFile) {
        val intent = Intent(requireContext(), DownloadService::class.java).apply {
            action = DownloadService.ACTION_START
            putExtra(DownloadService.EXTRA_URL, file.url)
            putExtra(DownloadService.EXTRA_FILE_NAME, file.name)
            putExtra(DownloadService.EXTRA_SAVE_PATH,
                Prefs.get().downloadPath.ifEmpty { requireContext().getExternalFilesDir(null)?.absolutePath ?: "" })
        }
        requireContext().startService(intent)
        Toast.makeText(requireContext(), "已添加下载: ${file.name}", Toast.LENGTH_SHORT).show()
    }

    private fun loadDirectory() {
        binding.tvPath.text = currentPath
        binding.progressBar.visibility = View.VISIBLE
        binding.tvEmpty.visibility = View.GONE
        adapter.clear()

        viewLifecycleOwner.lifecycleScope.launch {
            val result = hfsClient.listFiles(currentBaseUrl, currentPath)
            binding.progressBar.visibility = View.GONE
            result.onSuccess { files ->
                if (files.isEmpty()) {
                    binding.tvEmpty.visibility = View.VISIBLE
                    binding.tvEmpty.text = "目录为空"
                } else {
                    adapter.setFiles(files)
                }
            }.onFailure { e ->
                binding.tvEmpty.visibility = View.VISIBLE
                binding.tvEmpty.text = "加载失败: ${e.message}\n请确认目标已开启 HFS 服务"
            }
        }
    }

    override fun onDestroyView() {
        super.onDestroyView()
        _binding = null
    }
}
