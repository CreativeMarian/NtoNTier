package com.ntontier.client.ui.share

import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.Toast
import androidx.fragment.app.Fragment
import com.ntontier.client.databinding.FragmentShareBinding
import com.ntontier.client.util.Prefs

class ShareFragment : Fragment() {

    private var _binding: FragmentShareBinding? = null
    private val binding get() = _binding!!

    override fun onCreateView(inflater: LayoutInflater, container: ViewGroup?, savedInstanceState: Bundle?): View {
        _binding = FragmentShareBinding.inflate(inflater, container, false)
        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        super.onViewCreated(view, savedInstanceState)
        binding.etSharePort.setText(Prefs.get().hfsDefaultPort.toString())
        binding.etSharePath.setText(Prefs.get().downloadPath.ifEmpty { requireContext().getExternalFilesDir(null)?.absolutePath ?: "" })
        binding.btnStartShare.setOnClickListener {
            Toast.makeText(requireContext(), "文件共享服务将在后续版本支持\n当前版本仅支持浏览和下载其他成员的文件", Toast.LENGTH_LONG).show()
        }
        binding.btnStopShare.setOnClickListener {
            Toast.makeText(requireContext(), "文件共享服务未运行", Toast.LENGTH_SHORT).show()
        }
    }

    override fun onDestroyView() {
        super.onDestroyView()
        _binding = null
    }
}
