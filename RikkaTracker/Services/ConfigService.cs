using System;
using System.IO;
using System.Text.Json;
using RikkaTracker.Core.Models;
using RikkaTracker.Models;

namespace RikkaTracker.Services
{
    /*
     * @Author: trae + deepseek-V4-pro
     * @Date: 2026-06-23
     * @Desc: 配置服务接口。新增 ConfigChanged 事件，用于在配置变更时通知
     *        各 ViewModel 即时刷新（如切换视图模式时刷新时间轴和活跃应用页）。
     */
    public interface IConfigService
    {
        AppConfig Config { get; }
        void Save();
        void Load();
        /// <summary>
        /// 配置变更时触发。各 ViewModel 可订阅此事件以即时刷新数据。
        /// </summary>
        event Action? ConfigChanged;
    }

    public class ConfigService : IConfigService
    {
        private readonly string _configFilePath;
        private readonly ILoggerService _logger;
        private AppConfig _config = new();

        public AppConfig Config => _config;

        public ConfigService(ILoggerService logger)
        {
            _logger = logger;
            
            try 
            {
                // 遵循用户意图：使用安装目录下的 Config 文件夹
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string configDir = Path.Combine(baseDir, "Config");
                
                if (!Directory.Exists(configDir))
                {
                    Directory.CreateDirectory(configDir);
                    _logger.Info($"Created config directory at: {configDir}", LogCategory.Config);
                }
                _configFilePath = Path.Combine(configDir, "config.json");
                Load();
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to initialize ConfigService path structure.", ex, LogCategory.Config);
                // 即使失败也确保 config 对象不为空
                _config = new AppConfig();
                _configFilePath = string.Empty; 
            }
        }

        public void Load()
        {
            if (string.IsNullOrEmpty(_configFilePath) || !File.Exists(_configFilePath))
            {
                _logger.Info("Config file not found, initializing with defaults.", LogCategory.Config);
                _config = new AppConfig();
                // 默认数据路径设为安装目录下的 Data 文件夹
                _config.DataStoragePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
                Save();
                return;
            }

            try
            {
                string json = File.ReadAllText(_configFilePath);
                var deserialized = JsonSerializer.Deserialize<AppConfig>(json);
                if (deserialized == null)
                {
                    _logger.Warning("Config file is empty or invalid. Resetting to defaults.", LogCategory.Config);
                    _config = new AppConfig();
                }
                else
                {
                    _config = deserialized;
                    _logger.Info("Configuration loaded successfully.", LogCategory.Config);
                }
            }
            catch (JsonException jex)
            {
                _logger.Error("Config file is corrupted (JSON Error). Falling back to defaults.", jex, LogCategory.Config);
                _config = new AppConfig();
            }
            catch (Exception ex)
            {
                _logger.Error("Unexpected error while loading config.", ex, LogCategory.Config);
                _config = new AppConfig();
            }
        }

        /*
         * @Author: trae + deepseek-V4-pro
         * @Date: 2026-06-23
         * @Desc: 保存配置并在成功后触发 ConfigChanged 事件，通知各订阅者刷新。
         */
        public event Action? ConfigChanged;

        public void Save()
        {
            if (string.IsNullOrEmpty(_configFilePath)) return;

            try
            {
                string json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configFilePath, json);
                _logger.Info("Configuration saved successfully.", LogCategory.Config);
                // 通知所有订阅者配置已变更
                ConfigChanged?.Invoke();
            }
            catch (UnauthorizedAccessException uex)
            {
                _logger.Error($"Permission denied while saving config to {_configFilePath}. Try running as admin or moving the app folder.", uex, LogCategory.Config);
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to save configuration.", ex, LogCategory.Config);
            }
        }
    }
}
