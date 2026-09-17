using System.Runtime.CompilerServices;

// Win32 互操作类型保持 internal，只对本解决方案内部开放：
// 这样 API 表面不会泄漏到程序集之外，同时 Capture 层仍能直接用 GDI 抓屏。
[assembly: InternalsVisibleTo("MortarHUD.Capture")]

// App 项目的 AssemblyName 是 MortarHUD（发布出来就叫 MortarHUD.exe），
// 所以这里必须写程序集名而不是项目名。
[assembly: InternalsVisibleTo("MortarHUD")]
[assembly: InternalsVisibleTo("MortarHUD.Core.Tests")]
[assembly: InternalsVisibleTo("MortarHUD.Ocr.Tests")]
[assembly: InternalsVisibleTo("MortarHUD.Benchmark")]
