package com.ntontier.client.ui.members

import android.view.LayoutInflater
import android.view.ViewGroup
import androidx.recyclerview.widget.RecyclerView
import com.ntontier.client.databinding.ItemMemberBinding
import com.ntontier.client.scan.Member

class MemberAdapter(
    private val onItemClick: (Member) -> Unit
) : RecyclerView.Adapter<MemberAdapter.ViewHolder>() {

    private val items = mutableListOf<Member>()

    inner class ViewHolder(val binding: ItemMemberBinding) : RecyclerView.ViewHolder(binding.root)

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): ViewHolder {
        val binding = ItemMemberBinding.inflate(LayoutInflater.from(parent.context), parent, false)
        return ViewHolder(binding)
    }

    override fun onBindViewHolder(holder: ViewHolder, position: Int) {
        val member = items[position]
        holder.binding.tvIp.text = member.ip
        holder.binding.tvHostname.text = member.hostname.ifEmpty { "未知主机" }
        holder.binding.tvStatus.text = if (member.isOnline) "在线" else "离线"
        holder.binding.tvStatus.setTextColor(
            holder.binding.root.context.getColor(
                if (member.isOnline) android.R.color.holo_green_light else android.R.color.darker_gray
            )
        )
        holder.binding.tvHfs.text = if (member.hfsPort > 0) "HFS:${member.hfsPort}" else "无HFS"
        holder.binding.root.setOnClickListener { onItemClick(member) }
    }

    override fun getItemCount(): Int = items.size

    fun updateMember(member: Member) {
        val index = items.indexOfFirst { it.ip == member.ip }
        if (index >= 0) {
            items[index] = member
            notifyItemChanged(index)
        } else {
            items.add(member)
            notifyItemInserted(items.size - 1)
        }
        items.sortBy { it.ip }
    }

    fun clear() {
        items.clear()
        notifyDataSetChanged()
    }
}
