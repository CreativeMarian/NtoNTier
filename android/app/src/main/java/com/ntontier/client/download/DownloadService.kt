package com.ntontier.client.download

import android.app.Notification
import android.app.PendingIntent
import android.app.Service
import android.content.Intent
import android.os.IBinder
import android.util.Log
import androidx.core.app.NotificationCompat
import com.ntontier.client.NtoNApplication
import com.ntontier.client.R
import com.ntontier.client.ui.MainActivity
import com.ntontier.client.util.FormatUtil
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.launch
import java.util.concurrent.ConcurrentHashMap

class DownloadService : Service() {

    companion object {
        private const val TAG = "DownloadService"
        private const val NOTIFICATION_ID = 2001
        const val ACTION_START = "action_start"
        const val ACTION_PAUSE = "action_pause"
        const val ACTION_RESUME = "action_resume"
        const val ACTION_CANCEL = "action_cancel"
        const val EXTRA_URL = "extra_url"
        const val EXTRA_FILE_NAME = "extra_file_name"
        const val EXTRA_SAVE_PATH = "extra_save_path"
        const val EXTRA_TASK_ID = "extra_task_id"

        @Volatile
        private var instance: DownloadService? = null

        fun getInstance(): DownloadService? = instance
    }

    private val serviceScope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val tasks = ConcurrentHashMap<String, DownloadTask>()
    private val activeJobs = ConcurrentHashMap<String, Job>()
    private val listeners = mutableListOf<DownloadListener>()

    interface DownloadListener {
        fun onTaskAdded(task: DownloadTask)
        fun onTaskProgress(task: DownloadTask)
        fun onTaskCompleted(task: DownloadTask)
        fun onTaskFailed(task: DownloadTask, error: String)
        fun onTaskPaused(task: DownloadTask)
    }

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        instance = this
        Log.d(TAG, "DownloadService created")
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        when (intent?.action) {
            ACTION_START -> {
                val url = intent.getStringExtra(EXTRA_URL) ?: return START_NOT_STICKY
                val fileName = intent.getStringExtra(EXTRA_FILE_NAME) ?: url.substringAfterLast("/")
                val savePath = intent.getStringExtra(EXTRA_SAVE_PATH) ?: getExternalFilesDir(null)?.absolutePath ?: filesDir.absolutePath
                startDownload(url, fileName, savePath)
            }
            ACTION_PAUSE -> {
                intent.getStringExtra(EXTRA_TASK_ID)?.let { pauseTask(it) }
            }
            ACTION_RESUME -> {
                intent.getStringExtra(EXTRA_TASK_ID)?.let { resumeTask(it) }
            }
            ACTION_CANCEL -> {
                intent.getStringExtra(EXTRA_TASK_ID)?.let { cancelTask(it) }
            }
        }
        return START_STICKY
    }

    fun addListener(listener: DownloadListener) {
        if (!listeners.contains(listener)) listeners.add(listener)
    }

    fun removeListener(listener: DownloadListener) {
        listeners.remove(listener)
    }

    fun getTasks(): List<DownloadTask> = tasks.values.toList()

    private fun startDownload(url: String, fileName: String, savePath: String) {
        val taskId = "${System.currentTimeMillis()}_${url.hashCode()}"
        val task = DownloadTask(
            id = taskId,
            url = url,
            fileName = fileName,
            savePath = savePath,
            totalBytes = 0,
            downloadedBytes = 0,
            status = DownloadStatus.QUEUED,
            speed = 0,
            createdAt = System.currentTimeMillis()
        )
        tasks[taskId] = task
        listeners.forEach { it.onTaskAdded(task) }
        startForeground(NOTIFICATION_ID, buildNotification("正在下载: $fileName", 0))

        serviceScope.launch {
            try {
                task.status = DownloadStatus.DOWNLOADING
                val downloader = SegmentDownloader(task, object : SegmentDownloader.ProgressCallback {
                    override fun onProgress(downloaded: Long, total: Long, speed: Long) {
                        task.downloadedBytes = downloaded
                        task.totalBytes = total
                        task.speed = speed
                        listeners.forEach { it.onTaskProgress(task) }
                        updateNotification(task)
                    }

                    override fun onComplete() {
                        task.status = DownloadStatus.COMPLETED
                        task.downloadedBytes = task.totalBytes
                        activeJobs.remove(taskId)
                        listeners.forEach { it.onTaskCompleted(task) }
                        updateNotification(task)
                        checkStopSelf()
                    }

                    override fun onError(message: String) {
                        task.status = DownloadStatus.FAILED
                        task.errorMessage = message
                        activeJobs.remove(taskId)
                        listeners.forEach { it.onTaskFailed(task, message) }
                        updateNotification(task)
                        checkStopSelf()
                    }
                })
                activeJobs[taskId] = coroutineContext[Job]!!
                downloader.start()
            } catch (e: Exception) {
                activeJobs.remove(taskId)
                task.status = DownloadStatus.FAILED
                task.errorMessage = e.message ?: "Unknown error"
                listeners.forEach { it.onTaskFailed(task, task.errorMessage) }
                checkStopSelf()
            }
        }
    }

    fun pauseTask(taskId: String) {
        tasks[taskId]?.let { task ->
            task.status = DownloadStatus.PAUSED
            activeJobs.remove(taskId)?.cancel()
            listeners.forEach { it.onTaskPaused(task) }
            updateNotification(task)
        }
    }

    fun resumeTask(taskId: String) {
        tasks[taskId]?.let { task ->
            if (task.status == DownloadStatus.PAUSED || task.status == DownloadStatus.FAILED) {
                task.status = DownloadStatus.DOWNLOADING
                serviceScope.launch {
                    val downloader = SegmentDownloader(task, object : SegmentDownloader.ProgressCallback {
                        override fun onProgress(downloaded: Long, total: Long, speed: Long) {
                            task.downloadedBytes = downloaded
                            task.totalBytes = total
                            task.speed = speed
                            listeners.forEach { it.onTaskProgress(task) }
                            updateNotification(task)
                        }
                        override fun onComplete() {
                            task.status = DownloadStatus.COMPLETED
                            activeJobs.remove(taskId)
                            listeners.forEach { it.onTaskCompleted(task) }
                            checkStopSelf()
                        }
                        override fun onError(message: String) {
                            task.status = DownloadStatus.FAILED
                            task.errorMessage = message
                            activeJobs.remove(taskId)
                            listeners.forEach { it.onTaskFailed(task, message) }
                            checkStopSelf()
                        }
                    })
                    activeJobs[taskId] = coroutineContext[Job]!!
                    downloader.start()
                }
            }
        }
    }

    fun cancelTask(taskId: String) {
        tasks[taskId]?.let { task ->
            task.status = DownloadStatus.CANCELLED
            activeJobs.remove(taskId)?.cancel()
            tasks.remove(taskId)
            checkStopSelf()
        }
    }

    private fun updateNotification(task: DownloadTask) {
        val percent = FormatUtil.formatPercent(task.downloadedBytes, task.totalBytes)
        val text = when (task.status) {
            DownloadStatus.DOWNLOADING -> "${task.fileName} ${percent}% ${FormatUtil.formatSpeed(task.speed)}"
            DownloadStatus.PAUSED -> "${task.fileName} 已暂停"
            DownloadStatus.COMPLETED -> "${task.fileName} 下载完成"
            DownloadStatus.FAILED -> "${task.fileName} 下载失败"
            else -> task.fileName
        }
        val nm = getSystemService(android.app.NotificationManager::class.java)
        nm.notify(NOTIFICATION_ID, buildNotification(text, percent))
    }

    private fun buildNotification(text: String, progress: Int): Notification {
        val pendingIntent = PendingIntent.getActivity(
            this, 0,
            Intent(this, MainActivity::class.java).apply { putExtra("fragment", "downloads") },
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT
        )
        return NotificationCompat.Builder(this, NtoNApplication.CHANNEL_DOWNLOAD)
            .setContentTitle("NtoNTier 下载")
            .setContentText(text)
            .setSmallIcon(android.R.drawable.stat_sys_download)
            .setContentIntent(pendingIntent)
            .setProgress(100, progress, progress == 0)
            .setOngoing(progress in 1..99)
            .setPriority(NotificationCompat.PRIORITY_LOW)
            .build()
    }

    private fun checkStopSelf() {
        if (tasks.none { it.value.status == DownloadStatus.DOWNLOADING || it.value.status == DownloadStatus.QUEUED }) {
            stopForeground(STOP_FOREGROUND_REMOVE)
        }
    }

    override fun onDestroy() {
        Log.d(TAG, "DownloadService destroyed")
        instance = null
        serviceScope.cancel()
        super.onDestroy()
    }
}

enum class DownloadStatus {
    QUEUED, DOWNLOADING, PAUSED, COMPLETED, FAILED, CANCELLED
}

data class DownloadTask(
    val id: String,
    val url: String,
    val fileName: String,
    val savePath: String,
    var totalBytes: Long,
    var downloadedBytes: Long,
    var status: DownloadStatus,
    var speed: Long,
    val createdAt: Long,
    var errorMessage: String = ""
)
