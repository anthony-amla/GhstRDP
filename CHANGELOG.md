# Changelog

Tudo o que muda em cada versão do Ghst RDP. O formato segue o [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/) e as versões seguem o [SemVer](https://semver.org/lang/pt-BR/).

Cada versão nova entra num PR que sobe o `<Version>` em `src/GhstRDP/GhstRDP.csproj` e troca o `## [Não lançado]` pelo número e a data. O texto da seção vira a descrição do Release no GitHub e aparece em "Novidades" no site.

## [Não lançado]

### Corrigido
- Fechar uma aba de sessão não derruba mais o app em alguns servidores.
- Se o app não conseguir abrir, ele avisa e fecha em vez de ficar rodando escondido.
- O cofre não perde o que você digitou se trancar sozinho com o editor aberto.
- Mudar o relógio do Windows não pula mais o bloqueio por PIN errado.

### Segurança
- Senhas copiadas não vão para o histórico do Win+V nem para a área de transferência na nuvem.
- O login salvo para o mstsc é removido quando você troca o endereço, o usuário ou a senha do servidor.
- Desligar a prévia da última tela apaga as prévias já salvas.

## [1.0.0] - 2026-11-09

### Adicionado
- Cadastro de VPS e dedicados com status online e latência.
- Sessões RDP em abas dentro do app, ou abrindo no mstsc.
- Etiquetas coloridas por cliente e banner com a logo no card.
- Cofre de senhas protegido por PIN.
- Modo sigiloso que borra os IPs e as prévias.
- Instalador e atualização automática pelo GitHub.

[Não lançado]: https://github.com/GhosttyE/GhstRDP/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/GhosttyE/GhstRDP/releases/tag/v1.0.0
