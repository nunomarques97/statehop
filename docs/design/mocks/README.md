# Mocks — Phase 1, ecrã da timeline

Três direções para o ecrã principal da Phase 1.

**Escolhida: A — Faixa do Dia**, a 6 set 2026. O sistema visual
que saiu daí está em `DESIGN.md`. As direções B e C ficam como registo da
decisão, não são para manter atualizadas.

A direção A foi depois revista com três alterações: o cabeçalho de três
números veio da B, o texto de agrupamento de trocas curtas veio da B, e
"Sem utilização" passou a ler mais leve do que atividade real.

| Ficheiro | Direção | Screenshot |
|---|---|---|
| `faixa-do-dia.html` | **A — Faixa do Dia** | `shots/faixa-do-dia.png`, `shots/faixa-do-dia-dark.png` |
| `coluna-do-dia.html` | **B — Coluna do Dia** | `shots/coluna-do-dia.png`, `shots/coluna-do-dia-dark.png` |
| `cartoes-de-contexto.html` | **C — Cartões de Contexto** | `shots/cartoes-de-contexto.png` |

Todas as capturas a **1180 × 820**, que é o tamanho real da janela. Há também
uma verificação a 900 de largura (`shots/*-900.png`), porque a janela é
redimensionável.

**Nota de 7 set 2026:** os mocks usam seis variáveis de cor com nomes de
categoria (`--app-code`, `--app-web`, …). Essa nomenclatura foi **substituída**
no `DESIGN.md` por dez lugares neutros com atribuição estável por hash e
reparação de vizinhança. O aspeto da direção A não muda; os nomes das variáveis
no HTML ficam como estavam, porque o mock é o registo da decisão e não a fonte
de verdade. A fonte de verdade é o `DESIGN.md`.

O mock também assume **6 blocos**. Um dia real de uso dá **131**: é uma questão
de densidade em aberto.

## O que estes mocks decidem, e o que não decidem

**Decidem:** a organização da informação, a paleta e o que a cor codifica, a
escala tipográfica, a densidade, e o elemento assinatura de cada direção.

**Não decidem, e só a build real de WinUI 3 pode resolver:**

- **Materiais.** Mica e Acrylic são materiais do sistema, com amostragem do
  fundo do ambiente de trabalho. Aqui estão aproximados por uma cor sólida.
- **Cor de destaque do sistema.** O Windows deixa o utilizador escolher a sua;
  os mocks usam um azul fixo. Na app real, o marcador de "agora" deve seguir a
  cor de destaque do sistema.
- **Tipos de letra.** Segoe UI Variable tem eixos óticos que o Chrome não
  aplica da mesma maneira que o XAML.
- **Barras de deslocamento, foco de teclado, animações de entrada, contraste
  elevado e leitores de ecrã.** Tudo isso é comportamento de controlo nativo.

Os dados são realistas mas **compostos**: as apps são as que o utilizador usa de
facto, o dia não é literalmente o dia dele.

## Regras que estas direções já respeitam

De `DESIGN.md` (regras de produto, já fechadas):

- O dia inteiro cabe **sem scroll**.
- A cor **só codifica significado**: qual app, ausência de utilização, "agora".
  Nenhuma cor decorativa.
- Sem gráficos, sem pontuações, sem comparações, sem linguagem de IA.
- Sem enquadramento de instrumento ou de profiler.
- O diagnóstico da Phase 0 vive numa **afordância discreta** na barra de estado.
- Claro e escuro, ambos.
