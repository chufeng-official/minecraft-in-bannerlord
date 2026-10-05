## 变更与原因

## 验证

- [ ] `pwsh -File tools/AuditSource.ps1`
- [ ] `dotnet run --project tests/Diagnostics/Diagnostics.csproj -c Release`
- [ ] 涉及引擎 API 的变更已本机编译；没有实机验证的能力明确标注
- [ ] 没有提交游戏 DLL、资源、存档、密钥或本机路径
- [ ] 新文件许可与来源清晰，文档及测试已更新

实机测试结果/限制：
