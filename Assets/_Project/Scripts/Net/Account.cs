using System;
using System.Threading.Tasks;
using ProjectFossil.Match;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

namespace ProjectFossil.Net
{
    // The player's online account: a username and password on Unity Gaming Services, the same on itch and Steam
    // and on every computer. Online play, voice, the worldwide boards and (later) teams need it; offline practice
    // doesn't. The sign-in is remembered, so it's asked for once per computer.
    public static class Account
    {
        private const string UsernameKey = "ProjectFossil.AccountName";

        public static bool SignedIn => _ready && AuthenticationService.Instance.IsSignedIn && !string.IsNullOrEmpty(Username);
        public static string Username { get; private set; }
        public static string PlayerId => SignedIn ? AuthenticationService.Instance.PlayerId : null;
        public static string Status { get; private set; }
        public static bool Busy { get; private set; }

        private static bool _ready;
        private static Task<bool> _init;

        // Username: 3 to 20 letters, digits or . - @ _ ; password: 8 to 30 with an upper and a lower case letter, a
        // digit and a symbol (the service's own rules, checked here first so the message is plain).
        public const string UsernameRules = "3 to 20 letters, digits, or . - @ _";
        public const string PasswordRules = "8 to 30 characters, with upper and lower case, a digit and a symbol";

        public static string CheckUsername(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length < 3 || name.Length > 20) return "Username: " + UsernameRules + ".";
            foreach (char c in name)
                if (!(char.IsLetterOrDigit(c) && c < 128) && c != '.' && c != '-' && c != '@' && c != '_') return "Username: " + UsernameRules + ".";
            if (NameFilter.IsOffensive(name)) return "Please choose a different username.";
            return null;
        }

        public static string CheckPassword(string pass)
        {
            if (string.IsNullOrEmpty(pass) || pass.Length < 8 || pass.Length > 30) return "Password: " + PasswordRules + ".";
            bool up = false, low = false, digit = false, symbol = false;
            foreach (char c in pass)
            {
                if (char.IsUpper(c)) up = true;
                else if (char.IsLower(c)) low = true;
                else if (char.IsDigit(c)) digit = true;
                else symbol = true;
            }
            return up && low && digit && symbol ? null : "Password: " + PasswordRules + ".";
        }

        private static Task<bool> Init() => _init != null && !(_init.IsCompleted && !_init.Result) ? _init : _init = InitNow();

        private static async Task<bool> InitNow()
        {
            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized) await UnityServices.InitializeAsync();
                _ready = true;
                return true;
            }
            catch (Exception e)
            {
                Status = "Can't reach the online services. Check the internet connection.";
                Debug.Log($"[Account] Services unavailable: {e.Message}");
                return false;
            }
        }

        // At start: sign back in if this computer remembers an account. An anonymous session (left by an older
        // build) is dropped, since online play needs a named account.
        public static async void Restore()
        {
            if (Busy || !await Init()) return;
            var auth = AuthenticationService.Instance;
            if (auth.IsSignedIn || !auth.SessionTokenExists) return;
            Busy = true;
            try
            {
                await auth.SignInAnonymouslyAsync(); // with a remembered session this resumes that account
                var info = await auth.GetPlayerInfoAsync();
                if (string.IsNullOrEmpty(info?.Username)) { auth.SignOut(true); Username = null; }
                else SetSignedIn(info.Username);
            }
            catch (Exception e)
            {
                Debug.Log($"[Account] Couldn't resume the session: {e.Message}");
                Username = null;
            }
            finally { Busy = false; }
        }

        public static async void SignIn(string username, string password, Action done = null)
        {
            if (Busy) return;
            username = username?.Trim();
            Status = CheckUsername(username) ?? (string.IsNullOrEmpty(password) ? "Enter your password." : null);
            if (Status != null) return;
            if (!await Init()) return;
            Busy = true;
            Status = "Signing in...";
            try
            {
                var auth = AuthenticationService.Instance;
                if (auth.IsSignedIn) auth.SignOut(true);
                await auth.SignInWithUsernamePasswordAsync(username, password);
                SetSignedIn(username);
                done?.Invoke();
            }
            catch (AuthenticationException e) { Status = Explain(e, signUp: false); }
            catch (RequestFailedException e) { Status = Explain(e, signUp: false); }
            finally { Busy = false; }
        }

        public static async void SignUp(string username, string password, Action done = null)
        {
            if (Busy) return;
            username = username?.Trim();
            Status = CheckUsername(username) ?? CheckPassword(password);
            if (Status != null) return;
            if (!await Init()) return;
            Busy = true;
            Status = "Creating your account...";
            try
            {
                var auth = AuthenticationService.Instance;
                if (auth.IsSignedIn) auth.SignOut(true);
                await auth.SignUpWithUsernamePasswordAsync(username, password);
                SetSignedIn(username);
                done?.Invoke();
            }
            catch (AuthenticationException e) { Status = Explain(e, signUp: true); }
            catch (RequestFailedException e) { Status = Explain(e, signUp: true); }
            finally { Busy = false; }
        }

        public static void SignOut()
        {
            if (_ready && AuthenticationService.Instance.IsSignedIn) AuthenticationService.Instance.SignOut(true);
            Username = null;
            Status = "Signed out.";
            PlayerPrefs.DeleteKey(UsernameKey);
        }

        private static void SetSignedIn(string username)
        {
            Username = username;
            Status = null;
            PlayerPrefs.SetString(UsernameKey, username);
            PlayerPrefs.Save();
        }

        // The last username used on this computer, to fill the sign-in form.
        public static string LastUsername => PlayerPrefs.GetString(UsernameKey, "");

        private static string Explain(RequestFailedException e, bool signUp)
        {
            Debug.Log($"[Account] {(signUp ? "Sign-up" : "Sign-in")} failed: {e.ErrorCode} {e.Message}");
            string m = e.Message ?? "";
            if (m.IndexOf("exist", StringComparison.OrdinalIgnoreCase) >= 0 && signUp) return "That username is taken. Try another.";
            if (m.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0 && signUp) return "Password: " + PasswordRules + ".";
            if (e.ErrorCode == CommonErrorCodes.TransportError || e.ErrorCode == CommonErrorCodes.Timeout)
                return "Can't reach the online services. Check the internet connection.";
            return signUp ? "Couldn't create the account. Try a different username." : "Wrong username or password.";
        }
    }
}
