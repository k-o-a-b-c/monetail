# Codex repository-local Skills

This directory contains Microsoft WinUI guidance, monetail architecture rules, and coding-agent behavior guidelines.

- **WinUI 3 (7 skills):** Copied with their supporting reference files from [Microsoft winappCli](https://github.com/microsoft/winappCli/tree/a44d29cc46b302e8d45c2651065a2bbf50576f17/plugins/winui/agent-plugin/skills) at commit `a44d29cc46b302e8d45c2651065a2bbf50576f17`. The [microsoft/win-dev-skills](https://github.com/microsoft/win-dev-skills) marketplace links to the WinUI plugin maintained in that upstream repository.
- **Windows LOB XAML (1 skill):** Taken from the [Microsoft Learn Windows LOB XAML skill](https://learn.microsoft.com/en-us/windows/apps/get-started/line-of-business/windows-lob-xaml-skill) published example (consulted on 2026-11-15).
- **monetail WinUI architecture (1 skill):** Project-specific rules for screen ownership, incremental MainWindow separation, and dependency/state review. [SKILL.md](monetail-winui-architecture/SKILL.md) is maintained locally and is not part of the Microsoft upstream copy.
- **Karpathy-inspired guidelines (1 skill):** A concise monetail adaptation of the four [community guidelines](https://github.com/multica-ai/andrej-karpathy-skills/tree/main/skills/karpathy-guidelines) (MIT); not an official release by Karpathy. Applies to code work across the project.

Skills are discovered from `.agents/skills/<name>/SKILL.md`. WinUI skills apply to relevant Windows UI tasks; `karpathy-guidelines` applies to design, implementation, and review across the project. They supplement, not supersede, `AGENTS.md`, `UI_DESIGN.md`, and canonical specifications.

To update the WinUI skills, compare against a newer upstream commit and copy the complete skill directories (including references). Review changes before merging. For the LOB skill, compare against the Microsoft Learn example.
