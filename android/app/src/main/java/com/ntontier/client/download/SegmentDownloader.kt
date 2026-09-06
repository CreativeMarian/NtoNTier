package com.ntontier.client.download

import android.util.Log
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient
import okhttp3.Request
import java.io.File
import java.io.RandomAccessFile
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicLong

class SegmentDownloader(
    private val task: DownloadTask,
    private val callback: ProgressCallback
) {
    companion object {
        private const val TAG = "SegmentDownloader"
        private const val BUFFER_SIZE = 8192
        private const val SEGMENT_SIZE = 2 * 1024 * 1024L // 2MB per segment
        private const val DEFAULT_THREADS = 8
    }

    interface ProgressCallback {
        fun onProgress(downloaded: Long, total: Long, speed: Long)
        fun onComplete()
        fun onError(message: String)
    }

    private val client = OkHttpClient.Builder()
        .connectTimeout(15, TimeUnit.SECONDS)
        .readTimeout(30, TimeUnit.SECONDS)
        .writeTimeout(30, TimeUnit.SECONDS)
        .retryOnConnectionFailure(true)
        .build()

    private val totalDownloaded = AtomicLong(0)
    private var lastUpdateTime = System.currentTimeMillis()
    private var lastDownloaded = 0L
    private val lock = Any()

    suspend fun start() = withContext(Dispatchers.IO) {
        try {
            val fileSize = getFileSize()
            if (fileSize <= 0) {
                // Server doesn't support HEAD or no content-length, do single-threaded download
                singleThreadDownload()
                return@withContext
            }

            task.totalBytes = fileSize
            val outputFile = File(task.savePath, task.fileName)
            outputFile.parentFile?.mkdirs()

            // Pre-allocate file
            RandomAccessFile(outputFile, "rw").use { raf ->
                raf.setLength(fileSize)
            }

            val threadCount = DEFAULT_THREADS.coerceAtMost((fileSize / SEGMENT_SIZE).toInt().coerceAtLeast(1))
            val segmentSize = fileSize / threadCount

            Log.d(TAG, "Starting download: ${task.fileName}, size=$fileSize, threads=$threadCount")

            coroutineScope {
                for (i in 0 until threadCount) {
                    val start = i * segmentSize
                    val end = if (i == threadCount - 1) fileSize - 1 else (i + 1) * segmentSize - 1
                    launch {
                        downloadSegment(i, start, end, outputFile)
                    }
                }
            }

            if (task.status != DownloadStatus.PAUSED && task.status != DownloadStatus.CANCELLED) {
                callback.onComplete()
            }
        } catch (e: Exception) {
            Log.e(TAG, "Download failed", e)
            if (task.status != DownloadStatus.CANCELLED) {
                callback.onError(e.message ?: "Download failed")
            }
        }
    }

    private fun getFileSize(): Long {
        return try {
            val request = Request.Builder().url(task.url).head().build()
            client.newCall(request).execute().use { response ->
                if (response.isSuccessful) {
                    response.header("Content-Length")?.toLongOrNull() ?: 0
                } else 0
            }
        } catch (e: Exception) {
            0
        }
    }

    private fun downloadSegment(segmentId: Int, start: Long, end: Long, outputFile: File) {
        var currentStart = start
        var retries = 0
        val maxRetries = 5

        while (currentStart <= end && retries < maxRetries) {
            if (task.status == DownloadStatus.PAUSED || task.status == DownloadStatus.CANCELLED) {
                return
            }
            try {
                val request = Request.Builder()
                    .url(task.url)
                    .header("Range", "bytes=$currentStart-$end")
                    .build()

                client.newCall(request).execute().use { response ->
                    if (!response.isSuccessful && response.code != 206) {
                        throw Exception("HTTP ${response.code}")
                    }
                    response.body?.byteStream()?.use { input ->
                        RandomAccessFile(outputFile, "rw").use { raf ->
                            raf.seek(currentStart)
                            val buffer = ByteArray(BUFFER_SIZE)
                            var bytesRead: Int
                            while (input.read(buffer).also { bytesRead = it } != -1) {
                                if (task.status == DownloadStatus.PAUSED || task.status == DownloadStatus.CANCELLED) {
                                    return
                                }
                                raf.write(buffer, 0, bytesRead)
                                currentStart += bytesRead
                                totalDownloaded.addAndGet(bytesRead.toLong())
                                reportProgress()
                            }
                        }
                    }
                }
                return // Segment complete
            } catch (e: Exception) {
                retries++
                Log.w(TAG, "Segment $segmentId error (retry $retries): ${e.message}")
                if (retries < maxRetries) {
                    Thread.sleep(1000 * retries.toLong())
                } else {
                    throw e
                }
            }
        }
    }

    private fun singleThreadDownload() {
        try {
            val request = Request.Builder().url(task.url).build()
            client.newCall(request).execute().use { response ->
                if (!response.isSuccessful) {
                    callback.onError("HTTP ${response.code}")
                    return
                }
                val fileSize = response.header("Content-Length")?.toLongOrNull() ?: -1
                task.totalBytes = fileSize.coerceAtLeast(0)
                val outputFile = File(task.savePath, task.fileName)
                outputFile.parentFile?.mkdirs()
                response.body?.byteStream()?.use { input ->
                    outputFile.outputStream().use { output ->
                        val buffer = ByteArray(BUFFER_SIZE)
                        var bytesRead: Int
                        while (input.read(buffer).also { bytesRead = it } != -1) {
                            if (task.status == DownloadStatus.CANCELLED) return
                            output.write(buffer, 0, bytesRead)
                            totalDownloaded.addAndGet(bytesRead.toLong())
                            reportProgress()
                        }
                    }
                }
                callback.onComplete()
            }
        } catch (e: Exception) {
            callback.onError(e.message ?: "Download failed")
        }
    }

    private fun reportProgress() {
        val now = System.currentTimeMillis()
        synchronized(lock) {
            if (now - lastUpdateTime >= 500) {
                val downloaded = totalDownloaded.get()
                val elapsed = (now - lastUpdateTime).coerceAtLeast(1)
                val speed = ((downloaded - lastDownloaded) * 1000 / elapsed).coerceAtLeast(0)
                callback.onProgress(downloaded, task.totalBytes, speed)
                lastUpdateTime = now
                lastDownloaded = downloaded
            }
        }
    }
}
