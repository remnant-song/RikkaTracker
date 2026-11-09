/*
 * @Author: trae + GLM-5.2
 * @Date: 2026-06-30
 * @Desc: 日志系统核心枚举定义。
 *        - LogLevel：日志级别，从 Trace（最细粒度）到 Fatal（致命错误）
 *        - LogCategory：日志分类，用于区分不同模块/用途的日志，
 *          其中 Monitor/Web 类日志输出到独立的监控日志文件，
 *          System/DB/UI/Config 类日志输出到通用应用日志文件。
 */

namespace RikkaTracker.Core.Models
{
    /// <summary>
    /// 日志级别枚举，数值越大严重程度越高。
    /// </summary>
    public enum LogLevel
    {
        /// <summary>最细粒度跟踪，如变量值、状态变化（默认关闭）</summary>
        Trace = 0,
        /// <summary>调试信息，如函数进入/退出、中间结果</summary>
        Debug = 1,
        /// <summary>常规运行时信息，如服务启动/停止</summary>
        Info = 2,
        /// <summary>警告，非致命错误或降级行为</summary>
        Warning = 3,
        /// <summary>错误，操作失败或异常</summary>
        Error = 4,
        /// <summary>致命错误，应用即将崩溃</summary>
        Fatal = 5
    }

    /// <summary>
    /// 日志分类枚举。
    /// Monitor/Web 分类的输出到监控日志文件（mntr_yyyyMMdd.log），
    /// 其余分类输出到应用日志文件（app_yyyyMMdd.log）。
    /// </summary>
    public enum LogCategory
    {
        /// <summary>应用生命周期、配置、主题、更新等运维日志</summary>
        System = 0,
        /// <summary>活动追踪、进程监控等采集行为日志（→监控文件）</summary>
        Monitor = 1,
        /// <summary>WebSocket 连接、网页数据接收等 Web 监控日志（→监控文件）</summary>
        Web = 2,
        /// <summary>数据库操作、刷新、迁移等日志</summary>
        DB = 3,
        /// <summary>窗口创建、导航、主题切换等 UI 日志</summary>
        UI = 4,
        /// <summary>配置加载/保存/变更日志</summary>
        Config = 5
    }
}
