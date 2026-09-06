package com.ntontier.client.hfs

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient
import okhttp3.Request
import org.jsoup.Jsoup
import org.jsoup.nodes.Document
import org.jsoup.nodes.Element
import java.util.concurrent.TimeUnit

data class HfsFile(
    val name: String,
    val url: String,
    val isDirectory: Boolean,
    val size: Long = 0,
    val modified: String = ""
)

class HfsClient {

    companion object {
        private const val TAG = "HfsClient"
        private const val DEFAULT_TIMEOUT = 15_000L
    }

    private val client: OkHttpClient = OkHttpClient.Builder()
        .connectTimeout(DEFAULT_TIMEOUT, TimeUnit.MILLISECONDS)
        .readTimeout(DEFAULT_TIMEOUT, TimeUnit.MILLISECONDS)
        .writeTimeout(DEFAULT_TIMEOUT, TimeUnit.MILLISECONDS)
        .retryOnConnectionFailure(true)
        .build()

    suspend fun listFiles(baseUrl: String, path: String = "/"): Result<List<HfsFile>> = withContext(Dispatchers.IO) {
        try {
            val fullUrl = buildUrl(baseUrl, path)
            val request = Request.Builder().url(fullUrl).build()
            client.newCall(request).execute().use { response ->
                if (!response.isSuccessful) {
                    return@withContext Result.failure(Exception("HTTP ${response.code}: ${response.message}"))
                }
                val body = response.body?.string() ?: return@withContext Result.failure(Exception("Empty response"))
                val files = parseHfsPage(body, fullUrl)
                Result.success(files)
            }
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    private fun buildUrl(baseUrl: String, path: String): String {
        val cleanBase = baseUrl.trimEnd('/')
        val cleanPath = if (path.startsWith("/")) path else "/$path"
        return "$cleanBase$cleanPath"
    }

    private fun parseHfsPage(html: String, baseUrl: String): List<HfsFile> {
        val files = mutableListOf<HfsFile>()
        val doc: Document = Jsoup.parse(html)

        // Try HFS 2.x table format first
        val tableRows = doc.select("table tr")
        if (tableRows.size > 1) {
            for (row in tableRows) {
                val link = row.select("a").firstOrNull() ?: continue
                val href = link.attr("href")
                if (href.isEmpty() || href == "#" || href.startsWith("?")) continue
                val name = link.text().trim()
                if (name.isEmpty() || name == ".." || name == "Parent Directory") continue
                val isDir = href.endsWith("/") || row.select("img[alt*=dir]").isNotEmpty()
                val sizeText = row.select("td").getOrNull(2)?.text()?.trim() ?: ""
                val modified = row.select("td").getOrNull(1)?.text()?.trim() ?: ""
                val size = parseSize(sizeText)
                val fullUrl = resolveUrl(baseUrl, href)
                files.add(HfsFile(name, fullUrl, isDir, size, modified))
            }
            if (files.isNotEmpty()) return files
        }

        // Fallback: parse all links
        for (link in doc.select("a")) {
            val href = link.attr("href")
            if (href.isEmpty() || href == "#" || href.startsWith("?") || href.startsWith("http") && !href.contains(baseUrl.substringAfter("://").substringBefore("/"))) continue
            val name = link.text().trim()
            if (name.isEmpty() || name == ".." || name == "Parent Directory" || name == "Index of") continue
            val isDir = href.endsWith("/")
            val fullUrl = resolveUrl(baseUrl, href)
            if (!files.any { it.url == fullUrl }) {
                files.add(HfsFile(name, fullUrl, isDir))
            }
        }
        return files
    }

    private fun resolveUrl(baseUrl: String, href: String): String {
        return if (href.startsWith("http://") || href.startsWith("https://")) {
            href
        } else {
            try {
                val base = java.net.URI(baseUrl)
                val resolved = base.resolve(href)
                resolved.toString()
            } catch (e: Exception) {
                val cleanBase = baseUrl.substringBeforeLast("/")
                "$cleanBase/$href"
            }
        }
    }

    private fun parseSize(text: String): Long {
        if (text.isEmpty()) return 0
        val clean = text.trim().uppercase()
        return try {
            val num = clean.filter { it.isDigit() || it == '.' }.toDoubleOrNull() ?: return 0
            when {
                clean.contains("GB") || clean.contains("G") -> (num * 1024 * 1024 * 1024).toLong()
                clean.contains("MB") || clean.contains("M") -> (num * 1024 * 1024).toLong()
                clean.contains("KB") || clean.contains("K") -> (num * 1024).toLong()
                else -> num.toLong()
            }
        } catch (e: Exception) {
            0
        }
    }

    suspend fun getFileSize(url: String): Long = withContext(Dispatchers.IO) {
        try {
            val request = Request.Builder().url(url).head().build()
            client.newCall(request).execute().use { response ->
                response.header("Content-Length")?.toLongOrNull() ?: 0
            }
        } catch (e: Exception) {
            0
        }
    }
}
