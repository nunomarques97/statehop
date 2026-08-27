# PRODUCT — Statehop

## Posicionamento

> **Windows que se adapta ao que estás a fazer.**

Alternativas de tagline: *"Switch context. Your PC adapts."* /
*"Switch contexts, not windows."*

Não posicionar como "AI-powered Windows productivity assistant" —
genérico e já ocupado por Copilot/Recall. IA é implementação, não
posicionamento.

## Categoria

Context-aware Windows utility. Não é "workspace manager" (PowerToys já
domina isso de graça) nem "AI desktop assistant" (Microsoft está a
construir isso na própria plataforma — Recall/Click to Do).

## Core loop do produto

```
Observe → Understand (infer context) → Suggest → Learn → Act
```

O sistema observa uso local, infere em que contexto o utilizador está,
sugere ações (nunca executa destrutivamente sem aprovação), aprende com
as decisões do utilizador, e só executa autonomamente ações seguras e
já aprovadas.

## Utilizador-alvo (depois da Phase 5, se validar)

Power users: developers, gamers, creators, traders/analysts, gente com
muitos monitores, 20+ apps abertas, uso intensivo de teclado. Não
"todo o utilizador Windows".

## Concorrência (resumo)

| Concorrente | O que faz | Porque não mata a tese |
|---|---|---|
| PowerToys Workspaces | Guarda/lança grupos de apps, grátis, 135k+ stars | Estático, requer configuração manual; sem inferência |
| DisplayFusion ($34) | Perfis, hotkeys, scripts C#/VB.NET | "Configura tu próprio", não "eu descubro por ti" |
| Actual Window Manager ($59.95) | Regras profundas de startup/janelas | Não decide sozinho quando aplicar uma regra |
| ActivityWatch (grátis/OSS, 18.6k stars) | Observa atividade local | Só observa; não infere→sugere→age |
| ManicTime ($7/mês) / RescueTime ($7–16/mês) | Time tracking automático | Dashboards/produtividade abstrata, não ação concreta |
| AutoHotkey | Automação contextual poderosa | Exige scripting; nós vendemos zero-configuração |
| Power Automate Desktop | Automação geral | Workflow builder pesado; nós não temos builder nenhum |
| Raycast (Windows 2.0, ago 2026) | Launcher + AI layer | Valida a tese "camada inteligente utilizador↔PC", não é o mesmo produto |
| Microsoft Recall / Click to Do | Memória local de atividade + ações | Valida a direção; risco estrutural de longo prazo — motivo para não demorar anos a validar |

## MVP — o que construir

```
Local activity tracking (foreground app, processo, janela, idle, CPU/RAM)
+ context timeline (visualização simples do dia)
+ deteção automática de padrões (co-occurrence, clustering simples)
+ criação/lançamento manual de workspace
+ sugestões seguras de cleanup ("Docker parece não ser necessário agora")
+ preferências persistentes (never suggest / never close / always keep)
+ tray app, hotkey global, arranque com o Windows
```

## Fora de âmbito no MVP (não construir sem decisão explícita)

```
Voice assistant
Agente de IA totalmente autónomo
Screenshot recording / computer vision
Monitorização de conteúdo do browser
Conta / cloud / sync entre máquinas
Colaboração em equipa
Plugin marketplace
Linguagem de scripting/macros complexa
Controlo arbitrário do PC por LLM
Fecho automático de processos sem regra aprovada
Orquestração completa de virtual desktops
App mobile / suporte multi-plataforma
Integrações de calendário / Outlook / Gmail
"AI everything" — IA só entra onde ambiguidade de linguagem justifica
```

## Regra de IA

IA não deve ser introduzida antes da arquitetura determinística (Phases
0–3) estar sólida. Quando entrar, só como classificador de intenção em
linguagem natural, produzindo output estruturado (ex.:
`{"intent": "activate_workspace", "workspace": "Development",
"confidence": 0.94}`), nunca ação direta sobre o sistema. Um motor de
regras determinístico decide sempre o que fazer com essa intenção.

## Regra crítica de UX

**Inatividade não é equivalência a inutilidade.** Um processo com 0% de
CPU pode ser essencial (ex.: Docker). O wording de qualquer sugestão de
fecho deve ser probabilístico e reversível: *"Docker appears unlikely to
be needed in your current context"*, nunca *"Docker is inactive, so it
is safe to close"*.

## Níveis de segurança de ações

```
Level 0 — Observe only
Level 1 — Suggest (pergunta antes de agir)
Level 2 — Auto-act em ações seguras (abrir/focar app, restaurar posição)
Level 3 — Ações destrutivas (fechar apps) — requer autorização explícita
Level 4 — Force-kill — nunca automático por defeito
```

## Monetização (não é prioridade até Phase 5/6)

Modelo a testar, só depois de validação pessoal:

- **Free**: observer, context discovery, até 3 workspaces, sugestões
  básicas.
- **Pro — €19–29 one-time**: workspaces ilimitados, automação avançada,
  preferências aprendidas, regras avançadas, IA local/cloud opcional.
- **Eventual add-on cloud/AI/sync**: €3–5/mês, só com utilização real.

Não validar preço nem construir pricing page antes da Phase 5.

## Distribuição (só relevante pós-validação pessoal)

Canais: GitHub (issues/releases),
comunidades onde o problema já se discute (r/Windows11, r/PowerToys,
r/AutoHotkey, r/SideProject), Microsoft Store/winget quando estável,
Product Hunt/HN quando houver demo forte. Sem paid ads.
