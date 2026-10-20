using System;

namespace CmccEdu
{
    /// <summary>
    /// 登录编排。把「什么时候能用 cookie、什么时候必须人工、什么时候回退密码」
    /// 这套规则集中在一处，守护进程和面板都调它。
    /// </summary>
    internal static class LoginRunner
    {
        /// <summary>
        /// 自动登录（守护用）。永不弹窗、永不发短信。
        /// </summary>
        public static LoginResult AutoLogin(LoginSettings s)
        {
            if (Network.IsOnline()) return LoginResult.Success("已联网，跳过");

            using (var portal = new Portal(s.Portal, s.Ssid, s.AcName))
            {
                bool hasCookie = !string.IsNullOrWhiteSpace(s.AutoCookies);

                if (hasCookie)
                {
                    var rc = portal.CookieLogin(s);
                    if (rc.Ok) { rc.Mode = s.LoginMode; return rc; }

                    bool credentialGone = rc.Kind == "not-recognized" || rc.Kind == "rejected";
                    if (!credentialGone)
                    {
                        // 拿不到 IP、请求超时这类是环境问题，提示用户去输验证码毫无意义
                        AppLog.Write("cookie 登录暂时失败（" + rc.Message + "），稍后重试", "COOKIE");
                        return LoginResult.Fail("retry", "暂时无法认证：" + rc.Message + "，稍后自动重试");
                    }

                    AppLog.Write("凭据失效（" + rc.Message + "）", "COOKIE");
                    if (s.LoginMode == "dynamic")
                        return LoginResult.Fail("need-manual", "自动登录凭据已失效，请点面板上的「发送验证码并登录」");
                }

                // 动态密码必须人工触发，自动流程到此为止
                if (s.LoginMode == "dynamic")
                    return LoginResult.Fail("need-manual", "动态密码模式需要手动触发登录");

                // 静态密码可以全自动
                var pr = portal.Login(s, "password", null);
                pr.Mode = "static";
                return pr;
            }
        }

        /// <summary>手动登录（面板用）。mode: auto / cookie / sms / password。</summary>
        public static LoginResult ManualLogin(LoginSettings s, string mode, ILoginPrompter prompter)
        {
            if (mode == "auto") return AutoLogin(s);

            using (var portal = new Portal(s.Portal, s.Ssid, s.AcName))
            {
                if (mode == "cookie")
                {
                    if (string.IsNullOrWhiteSpace(s.AutoCookies))
                        return LoginResult.Fail("no-cookie", "没有保存 cookie，请改用「发送验证码并登录」");

                    var rc = portal.CookieLogin(s);
                    if (rc.Ok) { rc.Mode = s.LoginMode; return rc; }

                    bool credentialGone = rc.Kind == "not-recognized" || rc.Kind == "rejected";
                    if (credentialGone)
                        return LoginResult.Fail("need-manual",
                            "cookie 登录失败：" + rc.Message + "。请改用「发送验证码并登录」");
                    return LoginResult.Fail("retry", "暂时无法认证：" + rc.Message);
                }

                if (mode == "sms")
                {
                    // 用户明确要短信：跳过 cookie 直接走短信
                    var settings = Clone(s);
                    settings.LoginMode = "dynamic";
                    var sms = portal.Login(settings, "sms", prompter);
                    if (sms.Ok) sms.Mode = "dynamic";
                    return sms;
                }

                if (mode == "password")
                {
                    if (string.IsNullOrWhiteSpace(s.Password))
                        return LoginResult.Fail("no-password", "尚未保存密码，请填写后点「保存账号」");
                    var settings = Clone(s);
                    settings.LoginMode = "static";
                    var pw = portal.Login(settings, "password", prompter);
                    if (pw.Ok) pw.Mode = "static";
                    return pw;
                }

                return portal.Login(s, "cookie", prompter);
            }
        }

        public static LoginResult Logout(LoginSettings s)
        {
            using (var portal = new Portal(s.Portal, s.Ssid, s.AcName))
                return portal.Logout(s);
        }

        private static LoginSettings Clone(LoginSettings s)
        {
            return new LoginSettings
            {
                Username = s.Username,
                Password = s.Password,
                AutoCookies = s.AutoCookies,
                LoginMode = s.LoginMode,
                Portal = s.Portal,
                Ssid = s.Ssid,
                AcName = s.AcName,
                TriggerUrl = s.TriggerUrl,
                Autoload = s.Autoload,
                Interval = s.Interval
            };
        }
    }
}
