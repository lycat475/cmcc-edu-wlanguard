using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace CmccEdu
{
    /// <summary>
    /// 配置读写。刻意用「字典 + 原样回写」的方式，保证：
    ///  1) 与旧版 PowerShell 的 config.json 完全兼容（键名一致）；
    ///  2) 不认识的键不会被丢弃（旧版加的字段不会因为升级而消失）。
    /// </summary>
    internal sealed class Config
    {
        private const string DpapiPrefix = "dpapi:";
        private readonly Dictionary<string, object> _raw;

        private Config(Dictionary<string, object> raw) { _raw = raw; }

        // 文本缓存：文件没变就不重复读盘。
        // 注意只缓存"原文"，每次都重新解析成新对象——
        // 否则调用方改了配置（保存账号等）会互相污染。
        private static string _cachedText;
        private static DateTime _cachedStamp;
        private static long _cachedLength;

        public static Config Load()
        {
            // 首次运行（或用户误删）时自动生成一份默认配置，避免直接崩掉
            if (!File.Exists(AppEnv.ConfigPath))
            {
                var fresh = new Config(new Dictionary<string, object>());
                fresh.ApplyDefaults();
                fresh.Save();
                AppLog.Write("已生成默认 config.json", "CFG");
                return fresh;
            }

            var fi = new FileInfo(AppEnv.ConfigPath);
            string text;
            if (_cachedText != null && fi.LastWriteTimeUtc == _cachedStamp && fi.Length == _cachedLength)
            {
                text = _cachedText;
            }
            else
            {
                text = File.ReadAllText(AppEnv.ConfigPath, Encoding.UTF8);
                _cachedText = text;
                _cachedStamp = fi.LastWriteTimeUtc;
                _cachedLength = fi.Length;
            }

            var ser = new JavaScriptSerializer();
            var dict = ser.Deserialize<Dictionary<string, object>>(text);
            if (dict == null) throw new ConfigException("config.json 不是合法 JSON");
            return new Config(dict);
        }

        /// <summary>保存后作废缓存，避免读到旧的原文。</summary>
        private static void InvalidateCache()
        {
            _cachedText = null;
            _cachedStamp = default(DateTime);
            _cachedLength = -1;
        }

        // ---------------- 基础读写 ----------------

        public string GetString(string key)
        {
            object v;
            if (!_raw.TryGetValue(key, out v) || v == null) return "";
            return Convert.ToString(v);
        }

        public void SetString(string key, string value) { _raw[key] = value ?? ""; }

        public int GetInt(string key, int fallback)
        {
            int n;
            return int.TryParse(GetString(key), out n) ? n : fallback;
        }

        /// <summary>把字典原样写回（先写临时文件再原子替换，避免写一半断电毁配置）。</summary>
        public void Save()
        {
            var ser = new JavaScriptSerializer();
            string text = ser.Serialize(_raw);
            string tmp = AppEnv.ConfigPath + ".tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));

            if (File.Exists(AppEnv.ConfigPath))
            {
                try { File.Copy(AppEnv.ConfigPath, AppEnv.ConfigPath + ".bak", true); } catch { }
                try { File.Replace(tmp, AppEnv.ConfigPath, null); return; }
                catch { try { File.Delete(AppEnv.ConfigPath); } catch { } }
            }
            File.Move(tmp, AppEnv.ConfigPath);
            InvalidateCache();
        }

        // ---------------- 凭据 ----------------

        /// <summary>写入加密字段，同时清掉同名明文字段。</summary>
        public void SetSecret(string plainKey, string encKey, string plainValue)
        {
            _raw[encKey] = Protect(plainValue);
            _raw.Remove(plainKey);
        }

        public string GetSecret(string encKey, string plainKey)
        {
            string enc = GetString(encKey);
            if (!string.IsNullOrEmpty(enc)) return Unprotect(enc);
            return GetString(plainKey);   // 兼容还没迁移的旧配置
        }

        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(plain);
                byte[] enc = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
                return DpapiPrefix + Convert.ToBase64String(enc);
            }
            catch (Exception ex)
            {
                // 加密失败要让人知道，不能像旧版那样悄悄存明文
                AppLog.Write("DPAPI 加密失败，凭据将以明文保存: " + ex.Message, "CFG");
                return plain;
            }
        }

        public static string Unprotect(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return "";
            if (!stored.StartsWith(DpapiPrefix, StringComparison.Ordinal)) return stored;
            try
            {
                byte[] enc = Convert.FromBase64String(stored.Substring(DpapiPrefix.Length));
                byte[] bytes = ProtectedData.Unprotect(enc, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (Exception ex)
            {
                AppLog.Write("凭据解密失败（可能换了机器或用户）: " + ex.Message, "CFG");
                return "";
            }
        }

        // ---------------- 兼容旧版的默认值 ----------------

        public void ApplyDefaults()
        {
            SetDefault("portal", "http://192.0.2.1");
            SetDefault("acName", "0000.0000.000.00");
            SetDefault("ssid", "CMCC-EDU");
            SetDefault("triggerUrl", "http://www.baidu.com/");
            SetDefault("loginMode", "dynamic");
            if (GetInt("interval", 0) <= 0) _raw["interval"] = 60;
            SetDefault("autoload", "checked");
            SetDefault("guiStartMode", "tray");
        }

        private void SetDefault(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(GetString(key))) _raw[key] = value;
        }

        // ---------------- 校验 ----------------

        /// <summary>取出登录所需信息；配置不全时抛 ConfigException（由调用方决定怎么呈现）。</summary>
        public LoginSettings GetLoginSettings()
        {
            var s = new LoginSettings();
            s.Username  = GetSecret("usernameEnc", "username");
            s.Password  = GetSecret("passwordEnc", "password");
            s.AutoCookies = GetSecret("autoCookiesEnc", "autoCookies");
            s.LoginMode = GetString("loginMode");
            if (string.IsNullOrWhiteSpace(s.LoginMode)) s.LoginMode = "dynamic";
            s.Portal    = GetString("portal").TrimEnd('/');
            s.Ssid      = GetString("ssid");
            s.AcName    = GetString("acName");
            s.TriggerUrl = GetString("triggerUrl");
            s.Interval  = Math.Max(5, GetInt("interval", 60));
            s.Autoload  = GetString("autoload");

            if (string.IsNullOrWhiteSpace(s.Username))
                throw new ConfigException("尚未配置手机号，请在面板上方填写后点「保存账号」");
            if (s.LoginMode != "dynamic" && string.IsNullOrWhiteSpace(s.Password))
                throw new ConfigException("尚未配置密码，请在面板上方填写后点「保存账号」");
            return s;
        }
    }

    internal sealed class ConfigException : Exception
    {
        public ConfigException(string message) : base(message) { }
    }

    internal sealed class LoginSettings
    {
        public string Username;
        public string Password;
        public string AutoCookies;
        public string LoginMode;
        public string Portal;
        public string Ssid;
        public string AcName;
        public string TriggerUrl;
        public string Autoload;
        public int Interval;
    }
}
