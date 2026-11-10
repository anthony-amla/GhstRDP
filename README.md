# Ghst RDP

Gerenciador gratuito e open source de VPS e servidores dedicados via Área de Trabalho Remota, para Windows.

## Instalar

Baixe o `GhstRDPApp-win-Setup.exe` na página de [Releases](https://github.com/GhosttyE/GhstRDP/releases/latest) e execute.

- Não precisa de administrador nem de .NET instalado: o instalador já leva tudo e instala em `%LocalAppData%\GhstRDPApp`.
- Cria atalho no menu Iniciar e aparece em "Adicionar ou remover programas".
- Atualiza sozinho (pode desligar em Configurações → Atualizações).

> O executável ainda não é assinado digitalmente. Na primeira vez, o Windows pode mostrar o aviso do SmartScreen: clique em "Mais informações" → "Executar assim mesmo".

## Recursos

- **Sessões em abas:** usa o controle RDP do próprio Windows (`mstscax.dll`, o mesmo motor do mstsc). Tem reconexão automática e a resolução acompanha o tamanho da janela.
- **Abrir no mstsc:** gera o `.rdp` e grava a senha no Gerenciador de Credenciais do Windows.
- **Senhas criptografadas:** usam DPAPI, então só o seu usuário do Windows consegue ler.
- **Cofre de senhas com PIN:** PBKDF2-SHA256 e AES-256-GCM, com bloqueio automático.
- **Organização dos servidores:** status online, filtros por tipo, status e cliente, etiquetas coloridas e banner por servidor.
- **Modo sigiloso:** borra os IPs e as prévias, para gravar ou compartilhar a tela.

## Compilar

Requisitos: Windows 10/11 e .NET 10 SDK.

```powershell
dotnet build src\GhstRDP\GhstRDP.csproj
dotnet run --project src\GhstRDP\GhstRDP.csproj
```

## Publicar uma versão

1. Crie e envie uma tag:

   ```powershell
   git tag v1.1.0
   git push origin v1.1.0
   ```

2. O workflow `.github/workflows/release.yml` compila em modo self-contained, gera o instalador com [Velopack](https://velopack.io) e publica o Release com:
   - `GhstRDPApp-win-Setup.exe`: o instalador.
   - `GhstRDPApp-win-Portable.zip`: a versão portátil.
   - Os pacotes que o app usa para se atualizar sozinho.

Para gerar o instalador localmente:

```powershell
dotnet tool install -g vpk
dotnet publish src\GhstRDP\GhstRDP.csproj -c Release -r win-x64 --self-contained true -p:Version=1.1.0 -o publish
vpk pack -u GhstRDPApp -v 1.1.0 -p publish -e GhstRDP.exe --packTitle "Ghst RDP" -i src\GhstRDP\Assets\ghst.ico -o releases
```

## Dados

- `%APPDATA%\GhstRDP\`: servidores, configurações, `vault.json` (o cofre) e `banners\`. A gravação é atômica, com backup `.bak`.
- `%LOCALAPPDATA%\GhstRDP\`: arquivos `.rdp`, prévias das sessões e logs.
- `GHSTRDP_HOME=<pasta>`: usa outra pasta, útil para testar sem mexer nos dados reais.

Desinstalar o app não apaga esses dados.

## Logo e ícone

A arte original está em `art/`. Para regenerar `Assets/ghst.png` e `Assets/ghst.ico` (precisa do Python com Pillow):

```powershell
python tools\make-assets.py art\logo-source.webp src\GhstRDP\Assets
```

## Interop do RDP

`lib\MSTSCLib.dll` e `lib\AxMSTSCLib.dll` são gerados a partir do `mstscax.dll` do Windows. Para regenerar, rode `tools\gen-interop.ps1`.

## Licença

[MIT](LICENSE)
