using System;
using System.Runtime.InteropServices;

namespace Translater
{
    public static class Credentials
    {
        private const string TargetName = "Translater/DeepSeek";
        private const uint Generic = 1;
        private const uint PersistLocalMachine = 2;
        private const int NotFound = 1168;
        private const int MaxBlobBytes = 2560;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct Credential
        {
            public uint Flags;
            public uint Type;
            public string TargetName;
            public string Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint BlobSize;
            public IntPtr Blob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;
        }

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWrite(ref Credential credential, uint flags);

        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredDelete(string target, uint type, uint flags);

        [DllImport("advapi32.dll")]
        private static extern void CredFree(IntPtr credential);

        public static string Read()
        {
            IntPtr pointer;
            if (!CredRead(TargetName, Generic, 0, out pointer))
            {
                int error = Marshal.GetLastWin32Error();
                if (error == NotFound) return null;
                throw StorageError("读取", error);
            }

            try
            {
                var credential = (Credential)Marshal.PtrToStructure(pointer, typeof(Credential));
                if (credential.Blob == IntPtr.Zero || credential.BlobSize == 0 ||
                    credential.BlobSize > MaxBlobBytes || credential.BlobSize % 2 != 0)
                    throw new InvalidOperationException("保存的 DeepSeek 密钥格式异常，请在设置中重新保存。");

                return Marshal.PtrToStringUni(credential.Blob, (int)credential.BlobSize / 2);
            }
            finally
            {
                CredFree(pointer);
            }
        }

        internal static string NormalizeKey(string apiKey)
        {
            string key = (apiKey ?? "").Trim();
            if (key.Length == 0)
                throw new ArgumentException("请先在设置中填写 DeepSeek API Key。");
            if (key.Length > MaxBlobBytes / 2)
                throw new ArgumentException("DeepSeek API Key 过长，请检查复制的内容。");
            foreach (char ch in key)
            {
                if (ch < 33 || ch > 126)
                    throw new ArgumentException("DeepSeek API Key 格式有误，请从 DeepSeek 开放平台重新复制。");
            }
            return key;
        }

        public static void Save(string apiKey)
        {
            string key = NormalizeKey(apiKey);
            IntPtr blob = Marshal.StringToHGlobalUni(key);
            try
            {
                var credential = new Credential {
                    Type = Generic, TargetName = TargetName, UserName = "DeepSeek",
                    Persist = PersistLocalMachine, BlobSize = (uint)(key.Length * 2), Blob = blob
                };
                if (!CredWrite(ref credential, 0))
                    throw StorageError("保存", Marshal.GetLastWin32Error());
            }
            finally
            {
                Marshal.ZeroFreeGlobalAllocUnicode(blob);
            }
        }

        public static void Delete()
        {
            if (!CredDelete(TargetName, Generic, 0))
            {
                int error = Marshal.GetLastWin32Error();
                if (error != NotFound) throw StorageError("删除", error);
            }
        }

        private static Exception StorageError(string operation, int error)
        {
            return new InvalidOperationException("Windows 凭据管理器未能" + operation + "密钥（" + error + "）。");
        }
    }
}
