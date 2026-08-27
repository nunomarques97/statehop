# ADR 001 — Packaged (MSIX) vs unpackaged

**Data:** 27–28 ago 2026
**Blocking point:** B0.1

## Contexto

O orçamento estabelece que a Microsoft Store é a única via de
distribuição assinada viável dentro do orçamento (€0, sem SmartScreen),
e a Store exige MSIX. Mas MSIX traz identidade de pacote e um modelo de
"Desktop Bridge" que, historicamente, é confundido com o sandboxing
UWP/AppContainer — daí a necessidade de decidir com evidência, não com
suposição, porque o produto depende de enumerar processos de terceiros,
ler a janela em primeiro plano, detetar idle e (mais tarde) fechar
processos.

## Método

Documentação oficial (Microsoft Learn) **+ teste mínimo empírico**
nesta máquina (Windows 11 Home, build 26200, sessão não elevada):

1. Escrita de uma app de consola .NET 10 (`PkgSpike`, fora do repo, em
   `PkgSpike/`, descartável) que executa cinco verificações
   via P/Invoke puro (sem WinUI, para isolar o efeito do packaging da
   stack de UI):
   - Identidade de pacote (`Windows.ApplicationModel.Package.Current`).
   - Enumeração de processos (`Process.GetProcesses()` +
     `MainModule.FileName` de cada um).
   - Janela em primeiro plano (`GetForegroundWindow` +
     `GetWindowThreadProcessId`).
   - Deteção de idle (`GetLastInputInfo`).
   - Hotkey global (`RegisterHotKey` numa janela message-only).
   - `OpenProcess(PROCESS_TERMINATE, …)` sobre um processo próprio
     (Notepad lançado pela própria app) e sobre um processo SYSTEM
     (`services.exe`), sem chegar a matar nada.
2. Corrida **A — unpackaged**: `dotnet build` + execução direta do
   `.exe`.
3. Corrida **B — packaged**: empacotamento manual em MSIX
   (`AppxManifest.xml` com `rescap:Capability Name="runFullTrust"`,
   `EntryPoint="Windows.FullTrustApplication"`), assinado com um
   certificado de teste autoassinado (gerado e removido nesta sessão),
   instalado via `Add-AppxPackage` com o Developer Mode do Windows
   ativado para o efeito (ação confirmada antes de a
   fazer — é uma alteração de sistema, não só instalação de SDK), e
   executado diretamente a partir de
   `C:\Program Files\WindowsApps\<PackageFullName>\PkgSpike.exe`.
4. Pacote de teste e certificado **removidos no fim** desta sessão
   (`Remove-AppxPackage`, remoção do certificado de
   `Cert:\LocalMachine\TrustedPeople` e `Cert:\CurrentUser\My`). Nada
   disto fica instalado na máquina.

## Resultados (lado a lado)

| Verificação | Unpackaged | Packaged (MSIX, full trust) |
|---|---|---|
| `Package.Current` resolve | ❌ (`InvalidOperationException`, esperado) | ✅ `Statehop.PkgSpike_1.0.0.0_x64__x44vs2hqd2ayr` |
| Processos enumerados / `MainModule` acessível | 349 / 329 acessíveis | 332 / 312 acessíveis |
| `MainModule` negado | 20 — todos processos SYSTEM/protegidos (Idle, System, Secure System, Registry, smss, csrss, wininit) | 20 — **exatamente o mesmo conjunto** (Idle, System, Secure System, Registry, smss, csrss, wininit) |
| Foreground window | ✅ título+pid+nome corretos | ✅ idêntico |
| Idle detection (`GetLastInputInfo`) | ✅ funciona | ✅ funciona |
| Hotkey global (`RegisterHotKey`) | ✅ regista e desregista sem erro | ✅ idêntico |
| `OpenProcess(PROCESS_TERMINATE)` em processo próprio (Notepad filho) | ✅ permitido | ✅ permitido |
| `OpenProcess(PROCESS_TERMINATE)` em `services.exe` (SYSTEM) | ❌ negado | ❌ negado — **mesmo resultado** |

(A contagem total de processos difere ligeiramente entre as duas
corridas — 349 vs. 332 — porque não foram feitas em simultâneo, não
por causa do packaging; o padrão de acesso/negação é idêntico.)

**Conclusão empírica: não há diferença funcional observável entre app
packaged (full trust) e unpackaged para nenhuma das cinco capacidades
testadas.** A única restrição encontrada (acesso negado a processos
SYSTEM/protegidos) ocorre **igualmente nos dois modos** — é uma questão
de integrity level/ACL do processo alvo (relevante para B0.2, não para
B0.1), não uma restrição imposta pelo MSIX.

## Respostas às perguntas de partida

**1. Uma app WinUI 3 packaged (MSIX) consegue enumerar processos de
terceiros, ler a foreground window, detetar idle e registar hotkeys
globais sem privilégios elevados?**

Sim, confirmado empiricamente acima. A razão de fundo (documentação
oficial): um pacote MSIX declarado `runFullTrust` corre com um
processo Win32 normal, a integrity level **medium** — é o modelo
"Desktop Bridge", distinto do sandboxing AppContainer usado por apps
UWP restritas. Apps full-trust não estão isoladas por AppContainer e
mantêm acesso direto à maioria dos recursos do sistema, tal como uma
app unpackaged do mesmo utilizador
([MSIX containerization overview](https://learn.microsoft.com/en-us/windows/msix/msix-containerization-overview),
[Understanding how packaged desktop apps run on Windows](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-behind-the-scenes)).
A confusão comum ("MSIX = sandboxed") aplica-se a apps AppContainer,
não ao caso do Statehop.

**2. Como se faz arranque com o Windows em cada modelo?**

- **Unpackaged:** mecanismo clássico — chave de registo
  `HKCU\...\Run` ou atalho na pasta Startup do utilizador.
- **Packaged (MSIX):** esses dois mecanismos **deixam de funcionar**
  para apps packaged. O modelo correto é a extensão `StartupTask`
  declarada no `AppxManifest.xml`
  (`<desktop:Extension><desktop:StartupTask TaskId="..." Enabled="..."
  DisplayName="..." /></desktop:Extension>`), disponível desde o
  Windows 10 Anniversary Update para apps Desktop Bridge. O utilizador
  vê e controla isto no Task Manager → separador "Arranque", tal como
  qualquer outra app moderna
  ([StartupTask Class](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.startuptask?view=winrt-26100),
  [Supporting "launch at startup" in a desktop app converted with the Desktop Bridge](https://learn.microsoft.com/en-us/archive/blogs/appconsult/supporting-launch-at-startup-in-a-desktop-app-converted-with-the-desktop-bridge)).
  Isto é uma mudança de implementação a prever no spike da Phase 0, não um
  bloqueador.

**3. Que restrições existem para fechar processos de terceiros a
partir de uma app packaged?**

Nenhuma restrição *adicional* imposta pelo packaging em si, além do
que já se aplica a qualquer processo Win32 não elevado: não é possível
abrir/terminar processos que corram com integrity level mais alto
(SYSTEM, protegidos) sem elevação — confirmado empiricamente igual nos
dois modos. Isto é o âmbito de B0.2, a medir a sério no spike da Phase 0 com
as apps reais do utilizador (quantas caem em processos elevados).

**4. Recomendação**

**Seguir MSIX (packaged) desde o início**, confirmando a recomendação
por defeito. Trade-offs explícitos:

- **A favor:** caminho direto para a Microsoft Store (assinatura
  gratuita, sem SmartScreen); nenhuma perda de
  capacidade técnica encontrada nos testes acima; identidade de pacote
  dá acesso a APIs úteis mais tarde (ex.: `ApplicationData` para
  storage isolado, se desejado).
- **Custos reais, não bloqueadores:**
  - Arranque com o Windows exige `StartupTask` em vez do registo
    simples — mais código de manifesto, mas bem documentado.
  - Iteração local durante o desenvolvimento exige Developer Mode
    ativo (ou um pacote assinado) para sideload — irrelevante em uso
    normal via `dotnet run`/F5 no Visual Studio (que trata disto
    automaticamente via *single-project MSIX*
    ([Package your app using single-project MSIX](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/single-project-msix))),
    só relevante para scripts de teste como este ADR.
  - B0.4 (falsos positivos de antivírus) não foi testado aqui — o
    binário de teste não foi distribuído nem executado fora desta
    máquina; falso positivo de AV depende mais de reputação/assinatura
    do que de packaged vs. unpackaged, e a assinatura da Store ajuda
    aqui também.
- **Não foi encontrada nenhuma evidência de que MSIX inviabilize
  funcionalidade essencial.** Não há motivo para reabrir esta decisão
  sem novo dado.

## Fontes

- [MSIX containerization overview — Microsoft Learn](https://learn.microsoft.com/en-us/windows/msix/msix-containerization-overview)
- [Understanding how packaged desktop apps run on Windows — Microsoft Learn](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-behind-the-scenes)
- [MSIX AppContainer apps — Microsoft Learn](https://learn.microsoft.com/en-us/windows/msix/msix-container)
- [StartupTask Class — Microsoft Learn](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.startuptask?view=winrt-26100)
- [Supporting "launch at startup" in a desktop app converted with the Desktop Bridge — Microsoft Learn](https://learn.microsoft.com/en-us/archive/blogs/appconsult/supporting-launch-at-startup-in-a-desktop-app-converted-with-the-desktop-bridge)
- [Package your app using single-project MSIX — Microsoft Learn](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/single-project-msix)
- [Windows App SDK deployment guide for framework-dependent packaged apps — Microsoft Learn](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deploy-packaged-apps)
- Teste empírico: `PkgSpike/` (não versionado; resultados
  brutos citados na tabela acima, capturados em 27–28 ago 2026).

## Consequências

- `Statehop.App` (Tarefa 4) é criado como projeto WinUI 3 com
  *single-project MSIX packaging* habilitado por defeito
  (`WindowsPackageType=Desktop`, `Package.appxmanifest` presente),
  não `None`.
- O spike da Phase 0 deve implementar o arranque com o Windows via
  `StartupTask`, não via registo/Startup folder.
- B0.2 (processos elevados) continua por medir a sério com o uso real
  do utilizador — este ADR só mostra que a *causa* dessa restrição não é
  o packaging.
- Developer Mode do Windows foi ativado nesta máquina
  para permitir o teste; fica ativo — é um pré-requisito
  normal para desenvolvimento WinUI3/MSIX local, não algo a reverter.
