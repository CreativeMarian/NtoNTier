package com.ntontier.client.ui

import android.content.Intent
import android.net.VpnService
import android.os.Bundle
import android.view.MenuItem
import android.widget.Toast
import androidx.appcompat.app.ActionBarDrawerToggle
import androidx.appcompat.app.AppCompatActivity
import androidx.core.view.GravityCompat
import androidx.fragment.app.Fragment
import com.ntontier.client.R
import com.ntontier.client.databinding.ActivityMainBinding
import com.ntontier.client.ui.browse.FileBrowseFragment
import com.ntontier.client.ui.connect.ConnectFragment
import com.ntontier.client.ui.downloads.DownloadsFragment
import com.ntontier.client.ui.guide.GuideFragment
import com.ntontier.client.ui.members.MembersFragment
import com.ntontier.client.ui.more.MoreFragment
import com.ntontier.client.ui.networktest.NetworkTestFragment
import com.ntontier.client.ui.share.ShareFragment
import com.ntontier.client.vpn.N2NVpnService

class MainActivity : AppCompatActivity() {

    private lateinit var binding: ActivityMainBinding
    private lateinit var toggle: ActionBarDrawerToggle

    companion object {
        private const val VPN_REQUEST_CODE = 100
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityMainBinding.inflate(layoutInflater)
        setContentView(binding.root)

        setSupportActionBar(binding.toolbar)
        supportActionBar?.setDisplayHomeAsUpEnabled(true)

        toggle = ActionBarDrawerToggle(
            this, binding.drawerLayout, binding.toolbar,
            R.string.navigation_drawer_open, R.string.navigation_drawer_close
        )
        binding.drawerLayout.addDrawerListener(toggle)
        toggle.syncState()

        binding.navView.setNavigationItemSelectedListener { item ->
            handleNavigation(item.itemId)
            binding.drawerLayout.closeDrawer(GravityCompat.START)
            true
        }

        if (savedInstanceState == null) {
            replaceFragment(ConnectFragment(), getString(R.string.nav_connect))
            binding.navView.setCheckedItem(R.id.nav_connect)
        }

        // Handle intent from notification
        intent.getStringExtra("fragment")?.let { frag ->
            when (frag) {
                "downloads" -> {
                    replaceFragment(DownloadsFragment(), getString(R.string.nav_downloads))
                    binding.navView.setCheckedItem(R.id.nav_downloads)
                }
            }
        }
    }

    private fun handleNavigation(itemId: Int): Boolean {
        return when (itemId) {
            R.id.nav_connect -> {
                replaceFragment(ConnectFragment(), getString(R.string.nav_connect))
                true
            }
            R.id.nav_members -> {
                replaceFragment(MembersFragment(), getString(R.string.nav_members))
                true
            }
            R.id.nav_network_test -> {
                replaceFragment(NetworkTestFragment(), getString(R.string.nav_network_test))
                true
            }
            R.id.nav_share -> {
                replaceFragment(ShareFragment(), getString(R.string.nav_share))
                true
            }
            R.id.nav_browse -> {
                replaceFragment(FileBrowseFragment(), getString(R.string.nav_browse))
                true
            }
            R.id.nav_downloads -> {
                replaceFragment(DownloadsFragment(), getString(R.string.nav_downloads))
                true
            }
            R.id.nav_guide -> {
                replaceFragment(GuideFragment(), getString(R.string.nav_guide))
                true
            }
            R.id.nav_more -> {
                replaceFragment(MoreFragment(), getString(R.string.nav_more))
                true
            }
            else -> false
        }
    }

    private fun replaceFragment(fragment: Fragment, title: String) {
        supportFragmentManager.beginTransaction()
            .replace(R.id.fragment_container, fragment)
            .commit()
        supportActionBar?.title = title
    }

    fun checkVpnPermissionAndConnect() {
        val intent = VpnService.prepare(this)
        if (intent != null) {
            startActivityForResult(intent, VPN_REQUEST_CODE)
        } else {
            startVpnService()
        }
    }

    private fun startVpnService() {
        val vpnIntent = Intent(this, N2NVpnService::class.java).apply {
            action = "ACTION_CONNECT"
        }
        startForegroundService(vpnIntent)
    }

    fun disconnectVpn() {
        val vpnIntent = Intent(this, N2NVpnService::class.java).apply {
            action = "ACTION_DISCONNECT"
        }
        startService(vpnIntent)
    }

    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        if (requestCode == VPN_REQUEST_CODE) {
            if (resultCode == RESULT_OK) {
                startVpnService()
            } else {
                Toast.makeText(this, "VPN 权限被拒绝", Toast.LENGTH_SHORT).show()
            }
        }
    }

    override fun onOptionsItemSelected(item: MenuItem): Boolean {
        if (toggle.onOptionsItemSelected(item)) return true
        return super.onOptionsItemSelected(item)
    }

    @Deprecated("Deprecated in Java")
    override fun onBackPressed() {
        if (binding.drawerLayout.isDrawerOpen(GravityCompat.START)) {
            binding.drawerLayout.closeDrawer(GravityCompat.START)
        } else {
            super.onBackPressed()
        }
    }
}
