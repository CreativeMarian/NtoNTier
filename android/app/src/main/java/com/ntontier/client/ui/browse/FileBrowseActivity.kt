package com.ntontier.client.ui.browse

import android.content.Intent
import android.os.Bundle
import android.view.MenuItem
import android.view.View
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.LinearLayoutManager
import com.ntontier.client.databinding.ActivityFileBrowseBinding
import com.ntontier.client.download.DownloadService
import com.ntontier.client.hfs.HfsClient
import com.ntontier.client.hfs.HfsFile
import com.ntontier.client.util.Prefs
import kotlinx.coroutines.launch

class FileBrowseActivity : AppCompatActivity() {

    private lateinit var binding: ActivityFileBrowseBinding
    private lateinit var adapter: HfsFileAdapter
    private val hfsClient = HfsClient()
    private var currentPath = "/"
    private var baseUrl = ""
    private var memberIp = ""

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityFileBrowseBinding.inflate(layoutInflater)
        setContentView(binding.root)

        setSupportActionBar(binding.toolbar)
        supportActionBar?.setDisplayHomeAsUpEnabled(true)

        memberIp = intent.getStringExtra("member_ip") ?: ""
        val port = intent.getIntExtra("hfs_port", Prefs.get().hfsDefaultPort)
        baseUrl = "http://$memberIp:$port"

        supportActionBar?.title = "文件浏览 - $memberIp"

        adapter = HfsFileAdapter(
            onFileClick = { file -> onFileClick(file) },
            onDownloadClick = { file -> downloadFile(file) }
        )
        binding.recyclerView.layoutManager = LinearLayoutManager(this)
        binding.recyclerView.adapter = adapter

        binding.btnBack.setOnClickListener {
            if (currentPath != "/") {
                currentPath = currentPath.substringBeforeLast("/", "/")
                if (currentPath.isEmpty()) currentPath = "/"
                loadDirectory()
            }
        }

        loadDirectory()
    }

    private fun onFileClick(file: HfsFile) {
        if (file.isDirectory) {
            currentPath = file.url.substringAfter(baseUrl, "/")
            if (currentPath.isEmpty()) currentPath = "/"
            loadDirectory()
        } else {
            downloadFile(file)
        }
    }

    private fun downloadFile(file: HfsFile) {
        val intent = Intent(this, DownloadService::class.java).apply {
            action = DownloadService.ACTION_START
            putExtra(DownloadService.EXTRA_URL, file.url)
            putExtra(DownloadService.EXTRA_FILE_NAME, file.name)
            putExtra(DownloadService.EXTRA_SAVE_PATH,
                Prefs.get().downloadPath.ifEmpty { getExternalFilesDir(null)?.absolutePath ?: filesDir.absolutePath })
        }
        startService(intent)
        Toast.makeText(this, "已添加下载: ${file.name}", Toast.LENGTH_SHORT).show()
    }

    private fun loadDirectory() {
        binding.tvPath.text = currentPath
        binding.progressBar.visibility = View.VISIBLE
        binding.tvEmpty.visibility = View.GONE
        adapter.clear()

        lifecycleScope.launch {
            val result = hfsClient.listFiles(baseUrl, currentPath)
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
                binding.tvEmpty.text = "加载失败: ${e.message}"
            }
        }
    }

    override fun onOptionsItemSelected(item: MenuItem): Boolean {
        if (item.itemId == android.R.id.home) {
            finish()
            return true
        }
        return super.onOptionsItemSelected(item)
    }
}
