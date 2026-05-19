# Most-L10n-Tool
Most本地化工具，包含本地化Hook与语言文件提取器  
仅作开源目的，如果你需要自定义文本请自行提取语言文件

## Most-L10n-Hook
### 你需要修改*.Most.exe以实现本地化  
### Most.App.InitializeApplication
---
``` csharp
private void InitializeApplication(string[] args)
{
    Lesta.Application.Language.Languages = ...
    Most.L10n.HookManager.InitHook();
    ...
}
```