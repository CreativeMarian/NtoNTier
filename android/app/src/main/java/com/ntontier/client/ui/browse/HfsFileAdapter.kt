package com.ntontier.client.ui.browse

import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.recyclerview.widget.RecyclerView
import com.ntontier.client.databinding.ItemHfsFileBinding
import com.ntontier.client.hfs.HfsFile
import com.ntontier.client.util.FormatUtil

class HfsFileAdapter(
    private val onFileClick: (HfsFile) -> Unit,
    private val onDownloadClick: (HfsFile) -> Unit
) : RecyclerView.Adapter<HfsFileAdapter.ViewHolder>() {

    private val items = mutableListOf<HfsFile>()

    inner class ViewHolder(val binding: ItemHfsFileBinding) : RecyclerView.ViewHolder(binding.root)

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): ViewHolder {
        val binding = ItemHfsFileBinding.inflate(LayoutInflater.from(parent.context), parent, false)
        return ViewHolder(binding)
    }

    override fun onBindViewHolder(holder: ViewHolder, position: Int) {
        val file = items[position]
        holder.binding.tvFileName.text = file.name
        holder.binding.ivFileIcon.setImageResource(
            if (file.isDirectory) android.R.drawable.ic_menu_sort_by_size
            else android.R.drawable.ic_menu_save
        )
        holder.binding.tvFileInfo.text = if (file.isDirectory) {
            "文件夹"
        } else {
            "${FormatUtil.formatSize(file.size)} ${file.modified}"
        }
        holder.binding.btnDownload.visibility = if (file.isDirectory) View.GONE else View.VISIBLE
        holder.binding.root.setOnClickListener { onFileClick(file) }
        holder.binding.btnDownload.setOnClickListener { onDownloadClick(file) }
    }

    override fun getItemCount(): Int = items.size

    fun setFiles(files: List<HfsFile>) {
        items.clear()
        items.addAll(files.sortedWith(compareBy({ !it.isDirectory }, { it.name.lowercase() })))
        notifyDataSetChanged()
    }

    fun clear() {
        items.clear()
        notifyDataSetChanged()
    }
}
