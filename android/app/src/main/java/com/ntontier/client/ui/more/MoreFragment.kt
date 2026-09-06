package com.ntontier.client.ui.more

import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.Toast
import androidx.fragment.app.Fragment
import com.ntontier.client.databinding.FragmentMoreBinding
import com.ntontier.client.util.Prefs

class MoreFragment : Fragment() {

    private var _binding: FragmentMoreBinding? = null
    private val binding get() = _binding!!

    override fun onCreateView(inflater: LayoutInflater, container: ViewGroup?, savedInstanceState: Bundle?): View {
        _binding = FragmentMoreBinding.inflate(inflater, container, false)
        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        super.onViewCreated(view, savedInstanceState)
        binding.etDownloadThreads.setText(Prefs.get().downloadThreads.toString())
        binding.etDefaultPort.setText(Prefs.get().hfsDefaultPort.toString())
        binding.cbAutoConnect.isChecked = Prefs.get().autoConnect

        binding.btnSaveSettings.setOnClickListener {
            Prefs.get().downloadThreads = binding.etDownloadThreads.text.toString().toIntOrNull() ?: 8
            Prefs.get().hfsDefaultPort = binding.etDefaultPort.text.toString().toIntOrNull() ?: 8080
            Prefs.get().autoConnect = binding.cbAutoConnect.isChecked
            Toast.makeText(requireContext(), "设置已保存", Toast.LENGTH_SHORT).show()
        }

        binding.tvVersion.text = "版本 1.0.0"
        binding.tvAbout.text = "NtoNTier 安卓端\n基于 n2n 的 P2P 文件共享工具\n对应 Windows 端 v1.2.2"
    }

    override fun onDestroyView() {
        super.onDestroyView()
        _binding = null
    }
}
