# DESIGN — Statehop

**Estado:** as regras de produto abaixo estão **fechadas** e valem já. O
sistema visual (cores, escala tipográfica, espaçamento, raios, movimento)
fica por preencher até ser escolhida uma das três direções em
`docs/design/mocks/`.

Não escrever código de UI de produção antes dessa escolha.

---

## Regra de produto nº 1 — o Statehop nunca pode parecer vigilância

Decisão de 6 set 2026. Esta é uma regra de produto, não uma nota de
estilo, e ganha a qualquer outra consideração de design.

O Statehop observa **tudo** o que o utilizador faz, o dia inteiro. O desenho tem
de ler como **"isto é teu, e fica aqui"**, nunca como **"a tua atividade está
a ser medida"**.

Consequências vinculativas:

- **O produto observa e ajuda. Não classifica o utilizador.** Nada de
  pontuações de foco, nada de produtivo-versus-improdutivo, nada de streaks,
  nada de metas.
- **Nenhuma comparação.** Nem com outros utilizadores, nem com médias, nem com
  "a tua semana passada" apresentada como julgamento. Não há cloud e não vai
  haver.
- **Nada pode sugerir que os dados saem da máquina.** Quando houver dúvida, o
  ecrã di-lo explicitamente ("só nesta máquina").
- **Nenhuma linguagem de IA em lado nenhum da interface.** IA é implementação,
  não posicionamento (`docs/PRODUCT.md`).
- **Enquadramento de instrumento é proibido.** Vistas tipo profiler, "tracks",
  telemetria — mesmo quando tecnicamente elegantes — leem como ferramenta de
  vigilância. É a única coisa que este produto não pode ser.

Porquê, em concreto: o público-alvo são power users que **já rejeitaram o
RescueTime exatamente por isto**. A investigação de mercado de 27 ago
regista queixas de complexidade, subscrição, **transparência e privacidade**, e
conclui pela oportunidade de *"local-first + extremamente simples + ação
concreta"* em vez de *"tracking + dashboards + produtividade abstrata"*.

## Regra de produto nº 2 — o dia inteiro cabe sem scroll

O trabalho do ecrã principal é responder em dez segundos a "onde é que foi o
meu dia". Um dia de trabalho completo tem de caber na janela **sem scroll**.

Isto implica densidade, e a densidade vem da disciplina de instrumento sem o
enquadramento de instrumento: **a cor só codifica significado, nunca decora.**
Se uma cor não distingue uma app, um estado ou o "agora", não entra.

## Regra de produto nº 3 — nativo do Windows 11

Tom: **calmo, preciso, nativo.** A app deve parecer parte do Windows 11, não
uma página web dentro de uma janela. Funciona em tema claro **e** escuro; o
tema claro não é uma reflexão tardia.

## Regra de produto nº 4 — o wording das sugestões é probabilístico

Já em `docs/PRODUCT.md` e repetido aqui porque é regra de desenho de interface:
*"O Docker parece pouco provável de ser necessário neste contexto"*, nunca
*"O Docker está inativo, é seguro fechar"*. Inatividade não é inutilidade.

---

## Sistema visual — por preencher

Preencher a partir da direção escolhida, com: papéis de cor para claro e
escuro, escala tipográfica, escala de espaçamento, raios (no máximo 3 valores),
regras de movimento (um momento assinatura por ecrã) e uma lista de
faz/não-faz.
