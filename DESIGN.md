# DESIGN — Statehop

**Direção escolhida:** **A — Faixa do Dia**, a 6 set 2026.
Mock e capturas: `docs/design/mocks/faixa-do-dia.html` e
`docs/design/mocks/shots/`.

Razão da escolha, para se construir a intenção e não a imagem: **a faixa
responde a "onde é que foi o meu dia" numa só leitura, antes de qualquer texto
ser processado** — forma, proporção e a interrupção veem-se ao mesmo tempo. E
escala: é a mesma faixa com 6 blocos ou com 40.

Todo o trabalho de UI segue este ficheiro.

---

# Parte 1 — Regras de produto (fechadas)

Estas ganham a qualquer consideração estética. Não se reabrem sem uma decisão explícita.

## 1. O Statehop nunca pode parecer vigilância

O Statehop observa **tudo** o que o utilizador faz, o dia inteiro. O desenho tem
de ler como **"isto é teu, e fica aqui"**, nunca como **"a tua atividade está
a ser medida"**.

- **O produto observa e ajuda. Não classifica o utilizador.** Nada de
  pontuações de foco, produtivo-versus-improdutivo, streaks ou metas.
- **Nenhuma comparação.** Nem com outros, nem com médias, nem com "a tua
  semana passada" apresentada como julgamento. Não há cloud e não vai haver.
- **Nada pode sugerir que os dados saem da máquina.** Na dúvida, o ecrã di-lo:
  *"só nesta máquina"*.
- **Nenhuma linguagem de IA em lado nenhum da interface.** IA é implementação,
  não posicionamento (`docs/PRODUCT.md`).
- **Enquadramento de instrumento é proibido.** Vistas tipo profiler, "tracks",
  telemetria — mesmo tecnicamente elegantes — leem como vigilância.

Porquê, em concreto: o público-alvo são power users que **já rejeitaram o
RescueTime exatamente por isto** (queixas de transparência e privacidade), e a
resposta é a via *"local-first + extremamente simples + ação concreta"*.

## 2. O dia inteiro cabe sem scroll

O trabalho do ecrã principal é responder em dez segundos a *"onde é que foi o
meu dia"*. Um dia de trabalho completo cabe na janela **sem scroll**.

Daqui vem a densidade, e a densidade vem da disciplina de instrumento **sem** o
enquadramento de instrumento.

## 3. A cor só codifica significado, nunca decora

Se uma cor não distingue **uma app**, **uma ausência** ou **o "agora"**, não
entra. Não há cor de marca aplicada a superfícies, não há gradientes, não há
realces decorativos.

## 4. Nativo do Windows 11, claro e escuro

Tom: **calmo, preciso, nativo**. Deve parecer parte do Windows 11, não uma
página web dentro de uma janela. O tema claro **não** é uma reflexão tardia.

## 5. O wording das sugestões é probabilístico

*"O Docker parece pouco provável de ser necessário neste contexto"*, nunca
*"O Docker está inativo, é seguro fechar"*. Inatividade não é inutilidade
(`docs/PRODUCT.md`).

---

# Parte 2 — Sistema visual (direção A)

## Cores

Papéis, não nomes de cor. Os valores abaixo são o ponto de partida; o que é
vinculativo são os **papéis** e a regra 3.

| Token | Papel | Claro | Escuro |
|---|---|---|---|
| `surface` | Fundo da janela (onde o Mica assenta) | `#F9F9F9` | `#1F1F1F` |
| `layer` | Cartões e listas | `#FFFFFF` | `#2B2B2B` |
| `layer-alt` | Sulcos, fundo da faixa, estados hover | `#F3F3F3` | `#262626` |
| `stroke` | Contornos e separadores | `#E5E5E5` | `#383838` |
| `stroke-strong` | Contorno de ênfase | `#D2D2D2` | `#4A4A4A` |
| `text` | Texto principal | `#1B1B1B` | `#F2F2F2` |
| `text-2` | Secundário: datas, "com X", rótulos | `#5D5D5D` | `#B8B8B8` |
| `text-3` | Terciário: eixo, notas, **ausência** | `#8A8A8A` | `#8A8A8A` |
| `accent` | **Só** o marcador de "agora" | `#0F6CBD` | `#4CC2FF` |

### Paleta das apps

**Contradição resolvida a 7 set 2026.** A versão anterior tinha nomes de
categoria (`app-code`, `app-web`, …) e ao mesmo tempo uma regra de atribuição
por hash. As duas coisas não podem coexistir: sob um hash, o PokerStars cai em
`app-code`. Decisão: **nomes neutros de lugar, atribuição estável por hash.**
Classificar por categoria obrigaria a manter uma tabela de apps para sempre e
falharia em tudo o que fosse desconhecido — que nesta máquina é quase tudo.

Dez lugares, dessaturados de propósito e ordenados para que lugares
**consecutivos** também sejam distintos entre si (a reparação abaixo anda para
o lugar seguinte, por isso lugares vizinhos aparecem juntos com frequência).

| Token | Claro | Escuro | |
|---|---|---|---|
| `app-1` | `#3F6B8A` | `#6A9EC0` | azul |
| `app-2` | `#A8794F` | `#CFA274` | laranja queimado |
| `app-3` | `#4F8A7B` | `#6FB8A5` | verde-azulado |
| `app-4` | `#96566B` | `#C98098` | malva |
| `app-5` | `#7C6AA8` | `#A394D0` | violeta |
| `app-6` | `#7A8290` | `#98A2B2` | cinzento-azulado |
| `app-7` | `#A85F4F` | `#CF8A74` | terracota |
| `app-8` | `#3F7F8A` | `#6AB4C0` | ciano |
| `app-9` | `#5A6BA8` | `#8494D0` | índigo |
| `app-10` | `#6F8A4F` | `#9BBA74` | azeitona |
| `idle` | `#DCDCDC` | `#3A3A3A` | ausência — nunca é um lugar de app |

**1. Atribuição base: hash estável do nome normalizado.** FNV-1a do
`AppKey` (`AppNormalizer`), módulo 10. Estável entre sessões, entre dias e
entre máquinas: o utilizador aprende as cores do seu dia.

> **Armadilha de implementação:** não usar `string.GetHashCode()`. Em .NET é
> aleatorizado por processo, por isso as cores mudavam **a cada arranque da
> app** — exatamente o defeito que esta regra existe para evitar.

**2. Reparação de vizinhança.** Requisito: *dentro de um dia, cada app
com bloco visível tem de ser distinguível das suas vizinhas*. Só mais lugares
não chega — medido, ver abaixo. Por isso:

- As apps do dia com bloco são ordenadas por **tempo total, decrescente**.
- Cada uma fica no seu lugar de hash. Se esse lugar já estiver ocupado por uma
  app com que **é vizinha na faixa**, anda para o lugar seguinte livre.
- Como se percorre por tempo, **as apps dominantes nunca se mexem** — são
  precisamente aquelas cuja cor o utilizador decora.

**Porque é que isto é garantido e não uma probabilidade:** na faixa cada bloco
toca no máximo dois blocos, e a reparação só tem de fugir aos vizinhos já
colocados. Havendo mais lugares do que o grau máximo de adjacência do dia,
existe sempre lugar livre. No dia real de 6 set o grau máximo foi **8**, contra
10 lugares.

**3. Costura na faixa.** Entre segmentos há um fio de 1 px em `surface`. É a
rede de segurança estrutural: mesmo que um dia patológico esgote os lugares,
dois segmentos vizinhos **nunca leem como um só bloco**, que é o que atacava a
leitura de proporção. Se não houver lugar livre, a app fica no lugar de hash e
a costura resolve o resto.

**Medido num dia real de uso** (6 set 2026, 1 862 eventos de foreground,
131 blocos, 21 apps observadas, **13 com bloco**):

| | 8 lugares | 10 lugares | 12 lugares |
|---|---|---|---|
| Apps que mudam de lugar | 4 de 13 | **2 de 13** | 1 de 13 |
| Pares adjacentes com a mesma cor | 0 | **0** | 0 |

Sem reparação, e só a aumentar lugares, sobravam pares adjacentes iguais em
**todas** as contagens testadas (6, 8, 10, 12, 14 e 16 lugares). É por isso que
a reparação existe: mais cor é probabilidade, a reparação é garantia.

**Escolha: 10 lugares.** Doze reduziria os movimentos a um, mas doze cores
dessaturadas deixam de ser distinguíveis entre si, o que troca um problema
resolvido por outro pior.

**Custo residual, assumido:** uma app pode mudar de cor entre dias **se e só
se** colidir com uma vizinha nova. Na prática as apps de topo são estáveis, e
foram essas que a ordenação protegeu. A alternativa — estabilidade perfeita com
duplicados adjacentes ocasionais — foi rejeitada porque o requisito é o
da adjacência.

**As apps sem bloco não recebem cor.** O cabeçalho anuncia 21 apps, mas só 13
tiveram bloco. São números diferentes de propósito: só o segundo precisa de ser
colorido, e é isso que torna o problema tratável.

**A paleta não é semântica.** Nenhuma cor quer dizer "bom" ou "mau". Verde não
é produtivo, vermelho não é desperdício. Isto é a regra de produto 1 aplicada à
paleta.

## Tipografia

Segoe UI Variable, que é a família do sistema no Windows 11.

| Papel | Família | Tamanho | Peso |
|---|---|---|---|
| Título do dia | Segoe UI Variable **Display** | 28 | 600 |
| Números do cabeçalho | Segoe UI Variable Text | 17 | 600 |
| Cabeçalho de secção | Segoe UI Variable Text | 14 | 600 |
| Corpo, linhas de lista | Segoe UI Variable Text | 14 | 400 |
| Secundário, rótulos | Segoe UI Variable Text | 12–13 | 400 |
| Eixo, notas | Segoe UI Variable Text | 11 | 400 |

**Todos os números que se comparam usam algarismos tabulares**
(`font-variant-numeric: tabular-nums`; em XAML, a *feature* OpenType `tnum`).
Durações e horas alinham em coluna ou não se conseguem ler de relance.

## Espaçamento

Escala de 4: **4, 8, 12, 16, 24, 32**. Nada fora dela.
Margem da janela: 32 horizontal, 8 no topo (a barra de título já dá ar).

## Raios

Três valores, no máximo: **4** (elementos pequenos, amostras de cor), **6**
(faixa, botões), **8** (cartões e listas).

## O elemento assinatura: a faixa do dia

- Altura **84 px**, raio 6, cantos cortados (`overflow: hidden`).
- Fundo `layer-alt`: o sulco tem de se ver mesmo antes de haver dados.
- Um segmento por bloco contíguo, largura **proporcional à duração**. Sem
  largura mínima: um bloco de dois minutos deve **parecer** dois minutos.
- **Costura de 1 px em `surface` entre segmentos.** Dois blocos vizinhos nunca
  podem ler como um só — ver a regra de reparação da paleta.
- Ausência de utilização é um segmento `idle` — presente, não um buraco.
- Eixo por baixo, de hora a hora, em `text-3`, 11 px, tabular.
- **Marcador de "agora":** régua vertical de 2 px em `accent`, a transbordar
  6 px acima e abaixo da faixa, com o rótulo *agora* por baixo, alinhado à
  direita da régua. É o único uso de `accent` no ecrã.

## Estados

| Estado | Como se lê |
|---|---|
| **Atividade** | `text` a 400, cor da app na amostra, duração em `text` |
| **Ausência** (`Sem utilização`) | **Tudo em `text-3`**, incluindo hora e duração. Sem fundo diferente. É ausência, não um evento: **recua**, não se destaca. Um preenchimento chamaria mais atenção, não menos. |
| **A decorrer** | Termina em *"— agora"* em vez de uma hora de fim. Sem badge, sem pulsar. |
| **Sem dados ainda** | A faixa aparece vazia com o sulco visível e uma linha em `text-2`. Nunca um *spinner*: a app está a observar, não a carregar. |

## Movimento

Um momento assinatura por ecrã, e neste ecrã é **o marcador de "agora" a
avançar**. Mais nada anima por defeito.

- Sem animação de entrada de listas. As linhas novas aparecem; não deslizam.
- Sem *fade* na faixa a cada atualização.
- Respeitar sempre a definição do sistema de **reduzir movimento**.

## Faz / não faz

**Faz**
- Diz *"só nesta máquina"* onde o utilizador possa ter dúvida.
- Distingue **"ao computador"** de **"com utilização"**. São números
  diferentes, e dar só o primeiro inflacionaria o dia.
- Explica as próprias agregações em texto simples, junto do que agregam
  (ex.: *"Trocas de foco com menos de um minuto ficam agrupadas no bloco onde
  aconteceram."*).
- Põe o diagnóstico da Phase 0 atrás de uma **afordância discreta** na barra de
  estado.

**Não faz**
- Gráficos de fatias, sectores, KPIs em mosaico, medidores.
- Pontuações, metas, streaks, comparações.
- Emoji como ícones.
- Cartões dentro de cartões.
- Cor sem significado.
- A palavra "IA" ou equivalentes em qualquer parte da interface.

---

# Parte 3 — O que só a build real de WinUI 3 pode resolver

Os mocks são HTML. Decidem organização, paleta, tipografia, densidade e o
elemento assinatura. **Não** decidem o seguinte, e nenhuma destas linhas deve
ser tratada como fechada até existir uma captura da app a correr:

- **Mica e Acrylic.** São materiais do sistema, com amostragem do fundo do
  ambiente de trabalho. `surface` nos mocks é uma cor sólida a aproximá-los. Na
  app real, a janela deve usar **Mica**, e os cartões ficam por cima dele.
- **Cor de destaque do sistema.** O Windows deixa o utilizador escolher a sua.
  O marcador de "agora" deve **seguir a cor de destaque do sistema**, não o
  azul fixo dos mocks. O valor de `accent` na tabela é apenas o *fallback*.
- **Barras de deslocamento.** São controlo nativo, com o comportamento de
  sobreposição do Windows 11. Não estilizar.
- **Foco de teclado.** O anel de foco do WinUI, a ordem de tabulação e o acesso
  à faixa por teclado não existem nos mocks. A faixa **tem** de ser navegável
  por teclado, e cada bloco tem de ter nome acessível.
- **Contraste elevado.** Nesse modo, a paleta categórica é substituída pelas
  cores do sistema e a distinção entre apps passa a depender de texto, não de
  cor. Verificar antes de dar a Phase 1 por fechada.
- **Tema claro.** Verificar na máquina, não no mock: o Mica claro e o
  `layer` branco têm menos contraste entre si do que a aproximação sólida
  sugere.
- **Tipografia.** Segoe UI Variable tem eixos óticos que o navegador não aplica
  como o XAML.

**Definition of done de qualquer alteração de UI:** captura da **app a correr**
em claro e escuro, criticada contra este ficheiro, corrigida e recapturada. Um
build verde não é prova de que a UI existe — foi assim que se apanharam três
bugs reais na Phase 0.
