# Análise crítica dos métodos do PluginCAISM

Revisão estatística e matemática do pipeline de análise, da leitura do CSV até o que aparece
na tela e no modelo Revit. Escrita a partir da leitura integral de `Processamento/`,
`Dominio/`, `Configuracao/` e `Parsing/`, e das dúvidas levantadas ao longo das sessões de
trabalho neste repositório.

**Data:** 2026-07-30 · **Escopo:** `PluginCAISM/` (o simulador Grasshopper só entra onde
expõe uma premissa do plugin) · **Natureza:** documento de crítica. Não altera código.

---

## 0. Sumário executivo

O pipeline é bem escrito, bem comentado e honesto sobre boa parte das próprias limitações —
há oito `TODO(autor)` que já registram divergências entre o texto e o código. O problema não
é desleixo de implementação. É que **a cadeia inteira repousa sobre três premissas não
testadas**, e sobre elas foram empilhadas decisões estatísticas que, somadas, produzem
resultados coloridos com muito mais confiança do que os dados sustentam.

As três premissas:

1. que o diâmetro pupilar durante uma fixação reflete o **elemento fixado** e não a
   **luminância** daquele ponto da cena;
2. que fixações são observações **independentes**, de modo que `se = DP/√n` é válido;
3. que agrupar por **distância absoluta de 0,01 m** entre pontos de incidência detecta
   fixações.

Nenhuma se sustenta como está. A primeira é uma ameaça de validade que nenhum ajuste de
código resolve; a segunda e a terceira são corrigíveis.

### Achados por severidade

| # | Achado | Severidade | Corrigível no código? |
|---|---|---|---|
| A1 | Luminância não modelada — confundidor dominante da pupila | **Crítica** | Não (exige dado novo) |
| C3 | `se = DP/√n` trata fixações como independentes (pseudo-replicação) | **Crítica** | Sim |
| D1 | Usa quantil normal 1,96 com `se` estimado de n pequeno; α real é 12–19 %, não 5 % | **Crítica** | Sim (trivial) |
| D2 | Nenhuma correção para comparações múltiplas sobre dezenas de elementos | **Crítica** | Sim |
| B3 | ~23 % das linhas descartadas por `int.TryParse` em `EyeOpened` | ~~Alta~~ **RESOLVIDO** | ✅ 2026-07-30 |
| A2 | Limiar de estabilidade em metros, não em graus — viés contra objetos distantes | **Alta** | Sim |
| A3 | Grupos de 80–300 ms descartados silenciosamente (zona morta) | **Alta** | Sim |
| A4 | "Saccade" detectado não é um saccade — é um grupo estável de 2 linhas | **Alta** | Sim |
| C1 | `Objeto sem nome` é tratado como elemento legítimo e polui a unanimidade | **Alta** | Sim (trivial) |
| D3 | `PH_e` mistura escalas: numerador intra-participante, denominador total | **Alta** | Sim |
| C2 | Centragem força Σν̂ₑ = 0 — "estressor" é sempre relativo ao próprio participante | **Média** (conceitual) | Não sem mudar o modelo |
| E1 | PH por participante re-estima dentro do indivíduo → incomparável entre pessoas | **Média** | Sim (já diagnosticado no código) |
| F1 | Δcortisol cruza fases circadianas opostas E semanas diferentes | **Média** | Depende do desenho |
| A5 | Quantização: piso de 300 ms vira 355 ms a 17 Hz | **Média** | Sim |
| B1 | Baseline = mediana dos primeiros 60 s dentro da própria cena VR | **Média** | Depende do desenho |
| B2 | Minutos 1–2 e 4–5 nunca são usados (60 % da sessão descartada) | **Média** | Sim |
| C4 | Fixação multi-objeto entra na média de todos os elementos que tocou | **Média** | Sim |
| I1 | Pontuação ambiental calculada e exibida, mas não entra em nada | **Média** (apresentação) | Sim |
| H1 | Correlações sem p, sem IC, com n = 26 e 25 pares de eixos possíveis | **Média** | Sim |
| J1 | Tabela exibe `PH_e` ao lado da classificação, mas não é ele que classifica | **Média** (apresentação) | Sim |
| K1 | Nenhum teste automatizado no plugin | **Média** | Sim |
| G1 | Escores por soma simples, sem verificação de itens invertidos nem de completude | **Baixa** | Sim |

### O achado mais importante, em uma frase

**Com poucas fixações por elemento — o caso normal —, o teste que decide verde/vermelho tem
taxa de erro tipo I real entre 12 % e 19 % por elemento, e é aplicado a dezenas de elementos
sem correção.** Numa cena com 40 objetos, isso são **5 a 8 objetos coloridos por puro
acaso**, em dados onde não há efeito nenhum. Ver §D1 e §D2 para a conta.

---

## 1. O que o pipeline faz hoje

Fixando a cadeia antes de criticá-la. Só o Input 01 (CSV do HMD) alimenta a classificação;
cortisol e sensores não chegam ao classificador.

```
CSV do HMD
  └─ AnalisadorCsvSessao            linha a linha, 38 colunas
      └─ ConstruturDeSessoes        agrupa por (participante, sessão, cenário, assento)
          ├─ BaselinePupilar        mediana do diâmetro médio nos primeiros 60 s
          └─ DetectorDeMovimentoOcular
              ├─ filtra linhas      DadosOcularesValidos
              ├─ janela             120 000–240 000 ms
              ├─ agrupa             linhas consecutivas com Δ ≤ 0,01 m
              └─ classifica grupo   ≥300 ms → Fixação | 30–80 ms → Saccade | resto → descarte
                  └─ EstimadorDeEfeitoDeElemento         (Camada 1)
                      ├─ ΔD  = diâmetro médio da fixação − baseline da sessão
                      ├─ ΔD_c = ΔD − média dos ΔD do participante
                      ├─ agrupa por (objeto × cenário)
                      ├─ ν̂ₑ  = média dos ΔD_c do elemento
                      ├─ se  = DP amostral / √n
                      └─ σ_ΔD = DP dos ΔD BRUTOS de toda a amostra
                          └─ CalculadorDaCamada2         (Camada 2)
                              ├─ PH_e = tanh(−ν̂ₑ / σ_ΔD)
                              └─ classe por z = ν̂ₑ / se contra ±1,96
```

---

## 2. Parte A — Detecção de eventos oculares

### A1. Luminância não é modelada — e é o maior determinante do diâmetro pupilar

**Severidade: crítica. Ameaça de validade, não bug.**

O diâmetro pupilar responde a três coisas, em ordem de magnitude: **luz**, acomodação
(distância do alvo) e carga cognitiva/afetiva. O reflexo fotomotor move a pupila em vários
milímetros; a resposta cognitiva move décimos de milímetro. O modelo atribui **toda** a
variação de ΔD ao elemento fixado.

Numa cena de RV, elementos diferentes têm luminâncias diferentes por construção — uma
janela, uma luminária e um piso escuro não são comparáveis. O que `ν̂ₑ` mede, hoje, é em
primeiro lugar **quão claro é aquele objeto na cena**, e só depois, se sobrar variância,
qualquer coisa afetiva.

**Por que isso é suspeito e não apenas incompleto:** o sinal é *sistemático*, não ruído. Ele
não some com mais dados — ele fica mais significativo. Quanto mais fixações num objeto
claro, menor o `se`, maior o |z|, mais confiante a classificação **errada**. O modelo
converge para a resposta errada com confiança crescente. É o pior modo de falha possível.

Um teste diagnóstico que se pode rodar hoje, sem dado novo: correlacionar `ν̂ₑ` com a
luminância média do material do objeto no Revit. Se |ρ| > 0,5, a classificação está medindo
luz.

**Como melhorar, em ordem de esforço:**

1. **Barato e imediato — declarar a limitação.** Nenhuma figura deveria sair sem a ressalva
   de que o efeito não está desconfundido de luminância.
2. **Médio — extrair luminância do modelo.** O Revit conhece o material de cada elemento.
   Exportar a refletância/cor de cada objeto junto com o raycast do Grasshopper e incluir
   `luminância_e` como covariável na Camada 1 (uma regressão de ΔD_c contra luminância, com
   `ν̂ₑ` sendo o resíduo por elemento) remove a maior parte do confundimento.
3. **Melhor — desenho experimental.** Contrabalançar: o mesmo objeto apresentado em posições
   e níveis de iluminação diferentes entre participantes. Sem isso, elemento e luminância
   são colineares e nenhuma estatística os separa.
4. **Alternativa metodológica** — trocar a métrica. Índices como o **IPA** (*Index of
   Pupillary Activity*) ou o **LHIPA** foram desenhados justamente para serem robustos a
   mudanças de luminância, isolando a componente de alta frequência da pupila associada a
   carga cognitiva. Trocaria "diâmetro médio na fixação" por uma medida espectral da série
   pupilar. É a mudança mais defensável cientificamente.

### A2. O limiar de estabilidade está em metros, não em graus

**Severidade: alta.**

`ParametrosDeDeteccaoOcular.ToleranciaDeEstabilidadeMetros = 0.01` e
[`OlharEstaEstavel`](PluginCAISM/Processamento/DetectorDeMovimentoOcular.cs:147) compara a
**distância euclidiana 3D entre pontos de incidência consecutivos**.

Um limiar de dispersão do olhar é, por definição, **angular** — é uma propriedade do olho,
não da geometria da sala. Convertendo 0,01 m para o ângulo subtendido a partir do olho:

| distância do objeto | 0,01 m equivale a | velocidade angular máxima a 17 Hz |
|---|---|---|
| 1,2 m | 0,48° | 8,1 °/s |
| 4,0 m | 0,14° | 2,4 °/s |
| 10,0 m | 0,057° | 0,97 °/s |

A literatura de I-DT usa dispersão de **0,5° a 1,0°**; I-VT usa velocidade de **30 °/s**. O
plugin é, na melhor das hipóteses (objeto a 1,2 m), tão estrito quanto o extremo inferior da
faixa aceita; a 10 m é **17 vezes mais estrito** que o mínimo da literatura e **30 vezes**
mais estrito que o I-VT padrão.

**Consequência mensurável e enviesada:** para um mesmo movimento ocular, um objeto distante
produz um deslocamento em metros muito maior no ponto de incidência. Logo **objetos
distantes são sistematicamente menos capazes de sustentar uma fixação detectada**. Eles
recebem menos fixações, `n` menor, `se` maior, e caem em Neutro — não porque não afetem
ninguém, mas por trigonometria.

Isso interage de forma perversa com A1: objetos próximos (mobiliário, mesa, cadeira) são
super-representados; objetos distantes (parede do fundo, janela, teto) são
sub-representados. A conclusão do estudo fica enviesada em favor do mobiliário próximo.

**Como melhorar:** substituir a comparação métrica por angular. Já se tem a posição do olho
(`playerPosition`) e o ponto de incidência em cada linha, então o ângulo é direto:

```
v1 = pontoIncidencia(i-1) − posicaoOlho(i-1)
v2 = pontoIncidencia(i)   − posicaoOlho(i)
θ  = acos( (v1·v2) / (|v1||v2|) )
estável ⟺ θ ≤ ToleranciaDeEstabilidadeGraus     // 1.0° é o padrão defensável
```

Melhor ainda: migrar para **I-VT** (θ/Δt ≤ 30 °/s), que é o algoritmo que a própria
documentação do simulador diz estar emulando, e que é insensível à taxa de amostragem.

> **Nota de procedência.** Este é o ponto que forçou o simulador Grasshopper a escrever
> hit points **byte a byte idênticos** dentro de uma fixação — está documentado em
> `grasshopper/README.md`. Nas capturas reais o ponto deriva alguns milímetros por quadro e
> repetições exatas são < 1 % das linhas. Ou seja: **o detector só funciona confortavelmente
> em dado sintético construído para agradá-lo.** Isso é um sinal de alerta forte sobre o
> limiar, não sobre o simulador.

### A3. Zona morta: grupos de 80 a 300 ms são descartados sem registro

**Severidade: alta.**

[`ClassificarGrupos`](PluginCAISM/Processamento/DetectorDeMovimentoOcular.cs:170) faz:

```csharp
if (EhFixacao(duracaoMs))       fixacoes.Add(...);   // ≥ 300 ms
else if (EhSaccade(duracaoMs))  saccades.Add(...);   // 30–80 ms
// senão: nada. O grupo desaparece.
```

Nada trata a faixa **80–300 ms**, nem a faixa **0–30 ms**. A 16,9 Hz (intervalo de 59,2 ms),
a duração de um grupo de *n* linhas é `(n−1) × 59,2 ms`:

| n linhas | duração | destino |
|---|---|---|
| 1 | 0 ms | **descartado** |
| 2 | 59 ms | Saccade |
| 3 | 118 ms | **descartado** |
| 4 | 178 ms | **descartado** |
| 5 | 237 ms | **descartado** |
| 6 | 296 ms | **descartado** |
| 7 | 355 ms | Fixação |

Grupos de 3 a 6 linhas — plausivelmente uma fração grande dos dados — somem sem aparecer em
lugar nenhum: nem na análise, nem na auditoria, nem em um contador de descarte.

**Por que é suspeito:** um pipeline que descarta dados deve dizer quanto descartou. Hoje não
há como saber se 5 % ou 60 % dos grupos evaporaram. E a tabela de auditoria, que existe
justamente para mostrar o que o detector viu, **também não os mostra** — ela lista apenas
fixações e saccades.

**Como melhorar:**
1. Contar e reportar. Um campo `GruposDescartadosPorDuracao` no retorno do detector e uma
   linha no resumo da auditoria: *"N grupos entre 80 e 300 ms descartados"*.
2. Exibir os descartados na auditoria com um tipo próprio (`Indeterminado`), esmaecidos.
3. Rever a decisão. A literatura trata < 100 ms como ruído e ≥ 100 ms como fixação curta. Um
   piso de 300 ms é defensável se o objetivo for "fixação confortável" (é o que
   `DuracaoMinimaConfortavelDeFixacaoMs` sugere), mas então deveria haver uma categoria
   *fixação curta* separada, não um buraco.

### A4. O que o plugin chama de "saccade" não é um saccade

**Severidade: alta (conceitual).**

Um saccade é o **movimento** entre duas fixações — é, por definição, o intervalo em que o
olhar **não** está estável. O plugin monta grupos de linhas **estáveis** e depois chama de
saccade os grupos estáveis que duraram 30–80 ms.

Pela tabela acima, isso significa exatamente uma coisa: **um "saccade" é um grupo de 2
linhas**. É uma fixação curtíssima, ou ruído de amostragem — nunca o movimento.

O movimento verdadeiro, onde o olhar percorre a cena, produz linhas cujo ponto de incidência
salta mais de 0,01 m a cada quadro. Cada uma vira um grupo de 1 linha, duração 0 ms, e é
**descartada**. Os saccades reais nunca entram em lugar nenhum.

**Por que importa:** a auditoria exibe uma coluna TIPO com "Saccade" e o usuário
naturalmente lê "movimento entre fixações". O número está errado em conceito e em
magnitude — não mede o que o rótulo diz. Qualquer estatística de saccades (contagem,
amplitude, razão fixação/saccade) derivada disso é inválida.

**Como melhorar:** detectar saccades como o **complemento** das fixações. Depois de
identificadas as fixações, o intervalo entre o fim de uma e o início da seguinte é um
saccade, com amplitude = ângulo entre os centroides das duas fixações. Isso é o que a
literatura faz e o que o próprio simulador Grasshopper assume ao gerar os dados.

### A5. Quantização: o piso de 300 ms é na prática 355 ms

**Severidade: média.**

Como a duração é `(n−1) × intervalo`, o limiar efetivo depende da taxa de amostragem:

| taxa | intervalo | menor duração ≥ 300 ms | erro do piso |
|---|---|---|---|
| 8 Hz | 125 ms | 375 ms | +25 % |
| 17 Hz | 59 ms | 355 ms | +18 % |
| 30 Hz | 33 ms | 333 ms | +11 % |
| 120 Hz | 8,3 ms | 308 ms | +2,7 % |

**Por que é suspeito:** duas sessões gravadas em taxas diferentes não são comparáveis — a
mais lenta descarta mais fixações. Os arquivos reais têm intervalo variando de 45 a 81 ms
*dentro da mesma sessão*, então o piso efetivo oscila entre 315 e 405 ms de fixação para
fixação.

**Como melhorar:** medir a duração como `(n × intervalo médio)` em vez de
`(último − primeiro)`, ou — melhor — estender a duração meio intervalo para cada lado
(`último − primeiro + intervalo`), que é o estimador não enviesado do tempo coberto pelas
amostras. Corrige o viés sem mudar o limiar.

---

## 3. Parte B — Baseline, ΔD e validade das linhas

### B1. A baseline é medida dentro da própria cena, não em repouso

**Severidade: média. Decisão de desenho, não bug.**

[`CalcularBaselinePupilar`](PluginCAISM/Processamento/ConstruturDeSessoes.cs:150) toma a
**mediana** do diâmetro médio nos primeiros 60 s da sessão. A mediana é uma boa escolha
(robusta a piscadas e outliers). O problema é *onde* esses 60 s acontecem: já dentro do
ambiente virtual, já sob a iluminação da cena.

Isso tem duas consequências opostas e ambas ruins:

- **Se a cena é homogênea**, a baseline absorve a luminância global e ΔD fica limpo — bom.
- **Se o participante olhou para coisas específicas nesse primeiro minuto**, a baseline
  absorve o efeito desses objetos, e todos os demais são medidos *relativamente a eles*.
  Um objeto muito claro fixado no primeiro minuto puxa a baseline para cima e faz todo o
  resto da cena parecer restaurador.

Não há registro de o que foi fixado durante a janela de baseline, então não há como saber
qual dos dois casos ocorreu em cada sessão.

**Como melhorar:**
1. **Diagnóstico imediato:** reportar, por sessão, quantos objetos distintos foram fixados
   nos primeiros 60 s e o desvio-padrão do diâmetro nessa janela. Uma baseline com DP alto
   é uma baseline contaminada.
2. **Desenho:** incluir uma tela cinza neutra de 30–60 s antes da cena, com luminância
   média conhecida, e medir a baseline ali. É o padrão em pupilometria.
3. **Alternativa dentro do dado existente:** baseline *por objeto adjacente* — usar como
   referência os 200 ms imediatamente anteriores a cada fixação (*subtractive baseline
   correction*, padrão na literatura de pupilometria de eventos), em vez de uma baseline
   única por sessão. Isso controla deriva lenta e o estado de adaptação local à luz, que é
   exatamente o confundidor de A1.

### B2. 60 % da sessão nunca é usada

**Severidade: média.**

- 0–60 s: baseline
- 60–120 s: **nada**
- 120–240 s: análise
- 240 s–fim: **nada**

Numa sessão de 300 s, 120 s entram na análise. Os 60 s do meio e os 60 s finais são
descartados sem justificativa registrada no código — `JanelaTemporalInicioMs` e
`JanelaTemporalFimMs` estão em `Configuracoes.cs` sem comentário explicando a escolha.

**Por que é suspeito:** descartar dados é legítimo (habituação inicial, fadiga final), mas
precisa de razão declarada, e a razão precisa ser *pré-registrada*. Uma janela escolhida
depois de olhar os resultados é *p-hacking* mesmo sem intenção. Além disso, reduzir de 300 s
para 120 s corta o `n` por elemento pela metade ou mais — e `n` é exatamente o que está
faltando (ver D1).

**Como melhorar:**
1. Documentar a origem da janela 2–4 min no código e no texto.
2. **Análise de sensibilidade:** rodar o pipeline com janelas 2–4, 1–5 e 0–5 min e reportar
   se a classificação dos elementos muda. Se mudar muito, a janela é uma escolha
   arbitrária carregando o resultado, e isso precisa aparecer no texto.
3. Se a preocupação for habituação, modelar o tempo como covariável em vez de cortar.

### B3. `int.TryParse` em `EyeOpened` descarta ~23 % das linhas

> ## ✅ RESOLVIDO em 2026-07-30
>
> - `AnalisadorCsvSessao` lê as duas colunas com `LerDouble`; `LerInt` foi removido (não
>   tinha outro uso).
> - `LinhaDeSessao.OlhoEsquerdoAberto`/`OlhoDireitoAberto` (`int`) viraram
>   `AberturaDoOlhoEsquerdo`/`AberturaDoOlhoDireito` (`double`), com a natureza contínua da
>   coluna documentada.
> - `DadosOcularesValidos` compara contra
>   `ParametrosDeDeteccaoOcular.LimiarDeOlhoAberto = 0.5`, constante nova e documentada.
>
> **Efeito medido** sobre um CSV com abertura contínua realista (5 096 linhas):
>
> | | linhas válidas |
> |---|---|
> | regra antiga (`== 1` exato) | 3 868 (75,9 %) |
> | regra nova (`≥ 0,5`) | 5 008 (98,3 %) |
> | **recuperadas** | **1 140 (22,4 % do total)** |
>
> As 1,7 % que continuam inválidas são exatamente as piscadas — o limiar separa as duas
> populações como previsto. Compila limpo, sem avisos novos.
>
> **Consequência a observar:** com ~22 % mais linhas chegando ao detector, os grupos ficam
> mais longos e mais contínuos. Contagens de fixação, durações e `n` por elemento vão mudar
> em relação a qualquer resultado gerado antes desta data — inclusive para melhor no que diz
> respeito a §D1, já que `n` maior aproxima o quantil t de 1,96. Resultados anteriores não
> são comparáveis com os novos.

**Severidade: alta. Bug puro, correção trivial.** *(descrição original do problema abaixo)*

[`LerInt`](PluginCAISM/Parsing/AnalisadorCsvSessao.cs:202) lê `left_EyeOpened` /
`right_EyeOpened` como **inteiro**; qualquer valor que não seja o literal `1` — inclusive
`1.0` e `0.9663` — falha o parse, vira `0`, e a linha é lida como "olho fechado" por
[`DadosOcularesValidos`](PluginCAISM/Dominio/LinhaDesessao.cs:153).

Mas a abertura ocular nas capturas HTC é um sinal **contínuo**. As constantes medidas dos
dois arquivos reais (registradas em `SimulationDefaults` do simulador) dão a distribuição:

| faixa | proporção | destino no plugin |
|---|---|---|
| exatamente 1 | ~77 % | válida |
| [0,90 , 1,00) | ~17 % | **descartada** |
| [0,55 , 0,90] | ~6 % | **descartada** |

O `||` entre os dois olhos salva pouco: quando o esquerdo não é exatamente 1, o direito é
cópia dele ou um vizinho ruidoso, e quase nunca cai em `1` exato.

**Resultado: cerca de 23 % das linhas são jogadas fora como "olhos fechados", e ~17 pontos
percentuais delas eram olhos ≥ 90 % abertos.** Isso não é uma perda inócua — as linhas
removidas fragmentam grupos, encurtam fixações e, pela zona morta de A3, empurram grupos
para baixo do piso de 300 ms.

**Como melhorar:** ler como `double` e comparar com 0,5. O limiar é limpo: nas capturas
reais nenhuma amostra de piscada passa de 0,35 e nenhuma amostra normal fica abaixo de 0,55.

```csharp
// em vez de LerInt(...) == 1
double abertura = LerDouble(campos, mapeamento, "left_EyeOpened");
bool olhoEsquerdoAberto = abertura >= 0.5;
```

**Não** resolver isso mudando o simulador para escrever só 0/1: isso mascararia o problema
no dado sintético e deixaria o plugin igualmente quebrado no dado real.

---

## 4. Parte C — Camada 1 (estimação do efeito de elemento)

### C1. `Objeto sem nome` é tratado como um elemento legítimo

**Severidade: alta. Correção trivial.**

Quando o raycast falha, o logger Unity escreve `nomeDoObjeto = "Objeto sem nome"` e repete o
ponto de incidência anterior. Como o ponto é repetido, a linha **passa** no teste de
estabilidade e entra no grupo da fixação em curso.

Busquei a string em todo o plugin: **ela não aparece em lugar nenhum**. Não há filtro. Logo:

1. `ExtrairNomesDeObjetos` adiciona `"Objeto sem nome"` ao `HashSet` da fixação;
2. [`EstimarPorElemento`](PluginCAISM/Processamento/EstimadorDeEfeitoDeElemento.cs:200) cria
   um elemento chamado `Objeto sem nome × Cenário`, acumula ΔD_c nele, e a Camada 2 lhe
   atribui `ν̂ₑ`, `se`, `z` e uma classificação — que vai parar na tabela como se fosse um
   objeto do projeto;
3. como esse elemento aparece em muitas fixações, ele tende a ter `n` alto, `se` baixo, e
   portanto **boa chance de sair colorido com alta confiança**;
4. pior: a fixação agora nomeia dois objetos, e a regra de unanimidade em
   [`ResolverClassificacaoDaFixacao`](PluginCAISM/Dominio/ClassificacaoDePotencialDeHitpoint.cs:157)
   vê discordância e **rebaixa a fixação a Neutro** — inclusive no hitpoint do modelo Revit.

O item (4) é o mais insidioso: um único quadro inválido no meio de uma fixação legítima
apaga a cor daquela fixação no modelo 3D.

**Como melhorar:**
1. Declarar a constante em `Configuracao` (ela já existe do lado do simulador) e filtrá-la
   em `ExtrairNomesDeObjetos`, junto com o teste de string vazia.
2. Decidir explicitamente o que fazer com uma fixação cujos quadros inválidos passem de,
   digamos, 30 %: provavelmente descartá-la, com contagem reportada.

### C2. A centragem por participante força Σν̂ₑ = 0

**Severidade: média. Conceitual, inerente ao modelo escolhido.**

[`CentrarPorParticipante`](PluginCAISM/Processamento/EstimadorDeEfeitoDeElemento.cs:175)
subtrai de cada ΔD a média dos ΔD daquele participante. Isso é correto e necessário — sem
ele, quem tem pupila naturalmente maior arrasta para cima todos os elementos que olhou.

Mas tem uma consequência matemática exata que precisa ser dita em voz alta: **a média
ponderada por fixação de `ν̂ₑ` sobre todos os elementos é identicamente zero**.

Portanto:

- "Estressor" não significa *estressante*. Significa **acima da média deste participante
  nesta amostra**.
- Nenhuma cena pode ser majoritariamente estressora. Se todos os elementos de uma sala
  dilatarem a pupila igualmente, **todos saem Neutro**.
- Comparar duas cenas pelo balanço verde/vermelho é inválido: cada uma foi normalizada
  contra si mesma.

**Por que é suspeito na apresentação:** a legenda diz *"positivo = restaurador, negativo =
estressor"*, em termos absolutos. O leitor conclui "esta sala tem 12 elementos estressores".
A afirmação sustentada é "12 elementos estão acima da média desta amostra".

**Como melhorar:**
1. **Imediato, custo zero:** mudar a legenda e o rótulo. `Restaurador` → *"Abaixo da média
   (constrição relativa)"*, ou manter os nomes com uma nota fixa no rodapé da tabela.
2. **Estrutural:** para permitir afirmação absoluta é preciso uma **condição de controle** —
   uma cena neutra de referência que todo participante vê, contra a qual as demais são
   comparadas. É mudança de desenho experimental, não de código.

### C3. `se = DP/√n` supõe fixações independentes — elas não são

**Severidade: crítica.**

[`EstimarPorElemento`](PluginCAISM/Processamento/EstimadorDeEfeitoDeElemento.cs:235)
calcula `se(ν̂ₑ) = DP amostral dos ΔD_c / √n`, onde `n` é o número de fixações naquele
elemento, somadas todas as sessões e todos os participantes.

Essa fórmula vale para observações **independentes e identicamente distribuídas**. As
fixações não são nem uma coisa nem outra:

- **Agrupamento por participante.** Fixações do mesmo indivíduo compartilham estado
  fisiológico, fadiga, cafeína, hora do dia. Duas fixações do mesmo participante carregam
  muito menos informação que duas de participantes diferentes.
- **Autocorrelação temporal.** Fixações consecutivas ocorrem com segundos de diferença. A
  pupila tem constante de tempo da ordem de segundos — a componente lenta é praticamente a
  mesma nas duas.
- **Agrupamento por sessão.** Luminância e adaptação são compartilhadas dentro da sessão.

O efeito é conhecido e quantificável. Com correlação intraclasse ρ e `m` fixações por
participante, o **efeito de desenho** é `DEff = 1 + (m−1)·ρ`, e o `se` verdadeiro é
`se_ingênuo × √DEff`.

Com valores conservadores para pupilometria (ρ ≈ 0,3) e 10 fixações por participante:

```
DEff = 1 + 9 × 0,3 = 3,7      →      se verdadeiro ≈ 1,92 × se calculado
```

**O `se` está subestimado por um fator próximo de 2. O que significa que `z` está
superestimado por um fator próximo de 2, e a fronteira efetiva de decisão não é 1,96 mas
~3,8.** Quase tudo que hoje sai colorido no limiar é ruído.

**Como melhorar:**

1. **Correto e não muito caro — modelo misto.** É o que a Equação 3 do texto já pede e o
   código admite não implementar. `ΔD ~ 1 + (1|participante) + (1|elemento)` com o efeito de
   elemento como efeito aleatório dá `se` corretos por construção. Em C# sem biblioteca
   estatística isso é trabalhoso, mas a versão de dois níveis (participante, elemento) é
   implementável com EM ou com momentos.
2. **Barato e defensável — agregar antes de testar.** Em vez de tratar cada fixação como
   observação, calcular **uma média de ΔD_c por (participante × elemento)** e rodar a
   estatística sobre essas médias, com `n` = número de *participantes* que olharam aquele
   elemento. Isso elimina a pseudo-replicação de uma vez. O `n` cai muito — de talvez 40
   fixações para 8 participantes — e é exatamente esse o ponto: o `n` honesto sempre foi 8.
3. **Mínimo aceitável — inflar o `se`.** Aplicar `√DEff` com um ρ estimado dos próprios
   dados (variância entre participantes ÷ variância total). Uma linha de correção, muito
   melhor que nada.
4. **Bootstrap por participante.** Reamostrar participantes (não fixações) com reposição,
   recalcular `ν̂ₑ`, tomar o percentil 2,5–97,5. Não exige teoria, respeita o agrupamento, e
   é ~30 linhas de C#. **É o que eu recomendaria se só coubesse uma mudança.**

### C4. Fixação multi-objeto entra na média de todos os elementos que tocou

**Severidade: média.**

Documentado como decisão consciente (há um `TODO(autor)` no local), mas as consequências
estatísticas não estão registradas:

- **Σₑ nₑ > N.** A mesma observação é contada em vários elementos, então a soma dos `n` de
  todos os elementos excede o número de fixações. O conjunto de testes não é independente.
- **Correlação entre estimativas.** Dois objetos frequentemente fixados juntos (parede e
  janela, por exemplo) compartilham observações e têm `ν̂ₑ` correlacionados. Se um sai
  vermelho, o outro tende a sair vermelho — não por efeito próprio, mas por
  compartilhamento.
- **Diluição.** Se um objeto realmente afeta a pupila e o vizinho não, a fixação
  compartilhada empurra os dois para o meio: o verdadeiro efeito é subestimado e o nulo é
  superestimado.

**Como melhorar:** três opções, em ordem de preferência —
1. **Atribuição fracionária:** cada fixação contribui com peso `1/k` para cada um dos `k`
   objetos que tocou, e o `se` usa pesos. Corrige a contagem sem descartar dado.
2. **Atribuição ao objeto dominante:** o objeto que aparece na maioria das linhas da
   fixação. Simples e interpretável.
3. **Descartar fixações ambíguas** (`k > 1`) e reportar quantas foram. Mais conservador.

Se A2 for corrigido (limiar angular), o número de fixações multi-objeto deve cair
bastante — parte delas hoje é artefato de agrupar com tolerância métrica.

### C5. Detalhe fino: a centragem usa a média que inclui a própria observação

Menor, mas real. `μ̂ᵢ` é calculada sobre **todas** as fixações do participante, incluindo as
do elemento sendo estimado. Para um elemento que carrega grande parte das fixações de um
participante, `ν̂ₑ` é puxado para zero (a média subtraída já contém o efeito). Introduz viés
negativo proporcional à fração de fixações do participante naquele elemento.

**Como melhorar:** centragem *leave-one-element-out*, ou — melhor — resolver via modelo
misto (C3.1), onde o problema não existe.

---

## 5. Parte D — Camada 2 (índice e classificação)

### D1. Usar 1,96 com `se` estimado de `n` pequeno infla o erro tipo I para 12–19 %

**Severidade: crítica. Correção trivial e de grande efeito.**

[`Classificar`](PluginCAISM/Processamento/CalculadorDaCamada2.cs:81) compara
`z = ν̂ₑ / se(ν̂ₑ)` contra `ParametrosDaCamada2.QuantilNormal95 = 1.96`.

1,96 é o quantil da **normal padrão**. Ele só é válido quando o desvio-padrão do denominador
é **conhecido**. Aqui `se` é *estimado dos mesmos poucos dados* — a razão segue uma
distribuição **t de Student com n−1 graus de liberdade**, não uma normal.

A diferença não é acadêmica:

| n (fixações no elemento) | gl | quantil t correto (95 %) | α real ao usar 1,96 |
|---|---|---|---|
| 3 | 2 | 4,303 | **18,9 %** |
| 4 | 3 | 3,182 | **14,5 %** |
| 5 | 4 | 2,776 | **12,1 %** |
| 10 | 9 | 2,262 | 8,2 % |
| 20 | 19 | 2,093 | 6,5 % |
| 60 | 59 | 2,001 | 5,2 % |
| ∞ | — | 1,960 | 5,0 % |

Elementos com 3 a 5 fixações — extremamente comuns quando a janela de análise é de 2 minutos
e há dezenas de objetos na cena — estão sendo testados a um nível de significância real de
**12 % a 19 %**, três a quatro vezes o nominal.

**Como melhorar:** trocar a constante por um quantil t. Sem biblioteca estatística, uma
tabela de gl 1–30 mais a aproximação de Cornish-Fisher acima disso resolve com precisão de
sobra:

```csharp
// t_{0.975, gl} ≈ z + (z³ + z)/(4·gl) + (5z⁵ + 16z³ + 3z)/(96·gl²),  z = 1.96
public static double QuantilT95(int grausDeLiberdade) { ... }
```

Combinada com C3, essa correção provavelmente **remove a maior parte das cores da tela** —
e é exatamente por isso que ela precisa ser feita. As cores que sobrarem serão as
confiáveis.

### D2. Nenhuma correção para comparações múltiplas

**Severidade: crítica.**

Cada elemento é um teste de hipótese independente contra o mesmo limiar. Numa cena com `E`
elementos e um α por teste, o número esperado de falsos positivos sob a hipótese nula
completa é `α × E`:

| elementos na cena | α = 5 % (ideal) | α = 12 % (real, n = 5) |
|---|---|---|
| 15 | 0,8 falsos | 1,8 falsos |
| 40 | 2,0 falsos | **4,8 falsos** |
| 100 | 5,0 falsos | **12 falsos** |

Em uma cena de 40 objetos, sem nenhum efeito verdadeiro, o plugin **pinta cerca de 5 objetos
de verde ou vermelho**. E o modelo Revit gerado mostra exatamente esses cinco como
descoberta.

**Por que é especialmente suspeito aqui:** o produto final é um modelo 3D colorido. Um leitor
olhando a vista Revit não vê "5 de 40 testes"; vê "estes objetos são estressores". A forma de
apresentação amplifica o erro em vez de contextualizá-lo.

**Como melhorar:**
1. **Benjamini-Hochberg (FDR).** É o correto aqui — controla a proporção esperada de falsas
   descobertas entre as coloridas, e é muito menos conservador que Bonferroni. Precisa de
   p-valores (que hoje não são calculados, mas saem direto do t com os mesmos números), uma
   ordenação e uma comparação. ~20 linhas.
2. **Reportar a contagem esperada de falsos positivos** junto com a tabela, mesmo sem
   correção: *"40 elementos testados; sob a hipótese nula esperam-se ~2 coloridos por
   acaso"*. Custo quase zero, ganho grande de honestidade.
3. **Pré-registrar um subconjunto de elementos de interesse** e testar só esses. É a solução
   metodológica: 6 elementos hipotetizados a priori valem mais que 40 varridos.

### D3. `PH_e` mistura escalas: numerador intra-participante, denominador total

**Severidade: alta. Erro matemático de coerência.**

```
PH_e = tanh(−ν̂ₑ / σ_ΔD)
```

- `ν̂ₑ` é a média dos ΔD **centrados** — quantidade **intra-participante**, da qual a
  variância entre pessoas já foi removida;
- `σ_ΔD` é o desvio-padrão dos ΔD **brutos**, explicitamente não centrados
  ([EstimadorDeEfeitoDeElemento.cs:91](PluginCAISM/Processamento/EstimadorDeEfeitoDeElemento.cs:91)),
  contendo variância **entre + dentro** de participantes.

Dividir uma quantidade *within* por um desvio-padrão *total* é uma incoerência de escala.
Pela decomposição da variância, `σ²_total = σ²_entre + σ²_dentro`, então o denominador é
sempre maior que o correto, e **`PH_e` é sistematicamente comprimido em direção a zero** por
um fator `√(1 + σ²_entre/σ²_dentro)`.

Se a variabilidade entre participantes for comparável à interna — o que é a norma em
pupilometria —, o fator é ~1,4, e todos os `PH_e` estão ~30 % menores do que deveriam.

**Por que é suspeito:** o índice é apresentado como adimensional em (−1, +1) e comparável.
Ele não é comparável nem consigo mesmo entre amostras de composição diferente: mudar quantos
participantes entram na análise muda `σ_ΔD` e portanto muda o `PH_e` de todos os elementos,
mesmo que nenhum dado daquele elemento tenha mudado.

**Como melhorar:** usar o desvio-padrão **dentro de participantes** (o DP dos próprios ΔD_c,
que já é calculado em outro ponto) como denominador. É uma linha, e torna `PH_e` coerente
com `ν̂ₑ`. Documentar a mudança, porque altera todos os valores publicados.

### D4. `Neutro` acumula três situações diferentes

**Severidade: média (apresentação).**

`Neutro` hoje significa qualquer uma destas:

1. IC₉₅ contém zero, com `n` grande → **evidência de ausência de efeito**;
2. IC₉₅ contém zero, com `n` = 3 → **ausência de evidência**, que é coisa muito diferente;
3. `n < 2` ou dispersão nula → `se` não estimável → forçado a Neutro antes de qualquer teste.

O código documenta que Neutro é "insuficiência de evidência, não ausência de efeito" — mas a
interface não distingue os casos, e o hitpoint cinza no Revit é o mesmo nos três.

**Como melhorar:** separar visualmente. Um elemento com `n < 5` não deveria receber a mesma
cor de um com `n = 60` e IC estreito em torno de zero. Sugestão mínima: cinza claro para
"sem poder estatístico" (`n` abaixo de um limiar, ou meia-largura do IC maior que algum
efeito mínimo de interesse) e cinza escuro para "testado e nulo". E exibir o `n` e a largura
do IC na tabela, que já tem a coluna `Nº fix.` mas não a incerteza.

### D5. `PH_e` e a classificação podem discordar, e ambos aparecem lado a lado

**Severidade: média (apresentação).**

Está documentado no código, mas vale registrar como problema de comunicação: `PH_e` usa
`σ_ΔD` no denominador, a classificação usa `se(ν̂ₑ)`. São perguntas diferentes — "quão
grande é o efeito" versus "quão certo estou de que existe". Um elemento pode aparecer com
`PH = +0,82` e rótulo **Neutro**.

Isso já gerou confusão nesta própria sessão de trabalho. A tabela de Potencial
Homeodinâmico coloca as duas colunas lado a lado sem sinalizar que uma não decide a outra.

**Como melhorar:** exibir a **incerteza junto do efeito**. Trocar a coluna `PH (−1 a +1)`
por `PH [IC₉₅]`, mostrando `+0,82 [−0,10 , +1,00]`. O leitor vê imediatamente por que o
rótulo é Neutro. É a mudança de apresentação com melhor relação custo-benefício do
documento inteiro.

---

## 6. Parte E — Agregação por participante

### E1. O PH por participante re-estima o modelo dentro de cada indivíduo

**Severidade: média. Já diagnosticado no próprio código.**

`CalculadorDePotencialPorParticipante` chama o calculador de métricas **uma vez por
participante**. Dentro de cada chamada, a "amostra" é o próprio indivíduo: `μ̂ᵢ` é a média
dele mesmo (logo a centragem não faz nada — subtrai a média de si própria) e `σ_ΔD` é o
desvio dele mesmo.

O resultado é uma medida **puramente intra-individual**: dois participantes com pupilas
absolutas muito diferentes, mas com a mesma estrutura interna de ΔD, produzem PH idêntico.
As Figuras 54–56, que colocam um ponto por participante e correlacionam com IMC, idade e
IEEE, estão correlacionando essas quantidades com um número que **foi construído para não
variar entre pessoas**.

O código já registra isso em um `TODO(autor)` extenso e propõe a correção certa: estimar os
efeitos **uma vez** sobre todas as sessões de todos os participantes, e só então agregar.

**Como melhorar:** exatamente o que o TODO diz. E, enquanto não for feito, **não publicar as
Figuras 54–56** — a correlação medida ali não tem a interpretação pretendida. A classe está
hoje sem consumidor, o que é bom; o risco é ela voltar a ser usada sem que a correção tenha
sido feita.

---

## 7. Parte F — Cortisol

### F1. O Δ cruza fases circadianas opostas e semanas diferentes

**Severidade: média. Já registrado como `TODO(autor)`.**

```
Δcortisol = "SEMANA 3 FINAL DA SEMANA COLETA 3 ANTES DE DORMIR"
          − "SEMANA 1 INICIO DA SEMANA COLETA 1 AO ACORDAR"
```

O cortisol salivar tem um ritmo circadiano de amplitude enorme: o pico ao acordar (CAR) é
tipicamente **3 a 5 vezes** o valor noturno. Subtrair um do outro produz um número cuja
maior parte é a **descida circadiana normal do dia**, não o efeito do experimento — e ainda
por cima comparando semanas diferentes, o que adiciona variação entre-dias.

**Por que é suspeito:** o Δ resultante será **negativo para praticamente todos os
participantes**, com magnitude governada pela amplitude circadiana individual. Correlacioná-lo
com qualquer coisa mede, sobretudo, quão pronunciado é o ritmo circadiano da pessoa.

**Como melhorar:** comparar **coletas do mesmo horário** em semanas diferentes —
`SEMANA 3 AO ACORDAR − SEMANA 1 AO ACORDAR`, e separadamente `ANTES DE DORMIR` contra
`ANTES DE DORMIR`. Se o desenho tiver as três coletas diárias, o padrão-ouro é a **AUCg**
(área sob a curva em relação ao solo) ou a **AUCi** (em relação ao incremento), calculadas
pela regra trapezoidal — resumem o dia inteiro em um número e são as métricas usadas na
literatura de cortisol salivar.

O código de parsing já lê `AntesDeDormir` e as demais coletas; a informação está disponível.

---

## 8. Parte G — Formulários (IEEE, SBS)

### G1. Escores por soma simples, sem verificação

**Severidade: baixa a média.**

`CalculadorDeEscoresDeFormulario` soma os itens de cada domínio. Três lacunas:

1. **Itens invertidos.** Instrumentos de estresse normalmente têm itens com polaridade
   invertida, que precisam ser recodificados antes de somar. Não há nenhum tratamento disso,
   e há um `TODO(autor)` admitindo que a regra de agregação não foi confirmada.
2. **Completude.** `SomarItens` retorna a soma se **ao menos um** item foi encontrado. Um
   participante que respondeu 3 de 12 itens recebe um escore somado com os 3, diretamente
   comparável ao de quem respondeu 12. Isso é um erro silencioso e grave: escores de
   participantes com muitos itens faltantes são artificialmente baixos.
3. **O escore IEEE total é lido de uma coluna da planilha**, não recalculado a partir dos
   itens. Não há verificação de consistência entre o total informado e a soma dos itens.

**Como melhorar:**
1. Exigir uma taxa mínima de completude (ex.: 80 % dos itens) e devolver `NaN` abaixo dela,
   em vez de somar o que houver.
2. Quando houver faltantes aceitáveis, usar **imputação pela média dos itens respondidos
   escalada ao número total de itens** (`escore = média_respondidos × n_itens`), que é o
   procedimento padrão — em vez de soma parcial.
3. Recalcular o total IEEE dos itens e comparar com a coluna, reportando divergências.
4. Declarar explicitamente quais itens são invertidos (ou confirmar que nenhum é).

---

## 9. Parte H — Correlações

### H1. Sem p, sem IC, n = 26, 25 pares de eixos possíveis

**Severidade: média.**

`CalculadorDeCorrelacao` calcula Pearson e Spearman e **deliberadamente não produz
p-valores**, com a justificativa — registrada no código — de que reportar p sem correção
para 25 comparações seria pior que não reportar.

O raciocínio está certo pela metade. Omitir o p não elimina o problema de comparações
múltiplas: **transfere-o para o olho do usuário**, que vê um gráfico de dispersão com um `r`
grande e conclui exatamente o que o p corrigido teria desmentido. Com n = 26, o valor crítico
de |r| a 5 % é **0,388**; com correção de Bonferroni para 25 comparações (α = 0,002), sobe
para **0,570**. Um `r = 0,45` parece forte e é ruído.

**Como melhorar:**
1. Exibir o **intervalo de confiança** de `r` (via transformação z de Fisher — cinco linhas)
   em vez do ponto isolado. Um IC de `[−0,05 , 0,72]` comunica a incerteza sem exigir
   nenhuma decisão sobre correção.
2. Exibir junto o valor crítico de |r| para aquele n, corrigido pelo número de pares
   disponíveis na interface. Uma linha de rodapé: *"com n = 26 e 25 pares possíveis,
   |r| < 0,57 não é distinguível de acaso"*.
3. Marcar visualmente no gráfico quando |r| estiver abaixo do crítico.

---

## 10. Parte I — Variáveis ambientais

### I1. A pontuação ambiental é calculada, exibida, e não entra em nada

**Severidade: média (apresentação).**

`PontuarConfortoAmbiental` implementa uma pontuação de banda dupla razoável (1,0 dentro da
faixa de conforto, decaindo linearmente até 0 numa margem de 50 % da largura da faixa) para
temperatura, umidade, ruído e CO. Ela é calculada por elemento, exibida — e **não participa
do índice nem da classificação**, como o próprio comentário da classe declara.

Além disso:
- quando não há dado de sensor, retorna **0,5**, um valor que parece uma medição de
  conforto médio mas significa "não sei";
- o limite noturno de ruído (45 dB) não é aplicado porque a calculadora não recebe o período
  do dia — então cenas noturnas são avaliadas contra o limite diurno;
- o `TODO(autor)` em `EstimadorDeEfeitoDeElemento` registra que os quatro regressores
  ambientais da Equação 3 são **inexequíveis**: nenhuma amostra do `dados_hmd.csv` tem
  leitura de sensor casada por timestamp.

Esse último ponto merece destaque: o merge de sensores é feito por **igualdade exata de
`DateTime`** (`indiceDeSensores.TryGetValue(linha.Timestamp, ...)`). Amostras do HMD chegam a
~17 Hz e sensores ambientais tipicamente a cada minuto — a chance de casamento exato é
praticamente nula. **O merge nunca acerta**, e é por isso que não há dado ambiental.

**Como melhorar:**
1. **Consertar o merge:** casar pelo sensor mais próximo dentro de uma tolerância (ex.:
   ±30 s), não por igualdade exata. Isso sozinho pode habilitar toda a dimensão ambiental
   que hoje está morta.
2. Trocar o `0.5` de "sem dado" por `NaN` e exibir "—", para não simular uma medição.
3. Passar o período do dia à calculadora para aplicar o limite noturno correto.
4. Decidir: ou a pontuação ambiental entra no modelo (como covariável, que é o que a
   Equação 3 pede), ou sai da tabela. Exibir um número que não influencia nada convida a
   leitura causal indevida.

---

## 11. Parte J — Apresentação

### J1. A tela afirma mais do que o cálculo sustenta

Reunindo os pontos de apresentação já citados, porque juntos eles formam um padrão:

| O que a interface mostra | O que os dados sustentam |
|---|---|
| Hitpoint verde/vermelho no modelo 3D | Um teste com α real de 12–19 %, sem correção para dezenas de comparações |
| "Restaurador" / "Estressor" | "Abaixo / acima da média desta amostra" (§C2) |
| `PH (−1 a +1)` como número único | Um efeito cuja incerteza não é exibida (§D5) |
| Cinza uniforme | Três situações distintas confundidas (§D4) |
| Pontuação ambiental por objeto | Um número que não entra em nada e vale 0,5 quando não há dado (§I1) |
| `Fix. média (ms)` na tabela | Duração que não participa de nenhuma camada do modelo |
| Coluna TIPO = "Saccade" na auditoria | Grupos estáveis de 2 linhas, não movimentos (§A4) |

**Como melhorar — a mudança de maior impacto por menor esforço em todo este documento:**
adicionar **incerteza** onde hoje há apenas ponto. `PH` com IC, `ν̂ₑ` com IC, `n` visível, e
um rodapé com o número esperado de falsos positivos. Nada disso muda o cálculo; muda o que o
leitor conclui dele, que é o que está errado hoje.

### J2. A auditoria (recém-alterada) e o que ela agora mostra

A tabela de auditoria passou a classificar **cada fixação isoladamente**, com o ΔD centrado
exibido ao lado, e saccades sempre cinza. Isso corrige uma redundância — antes ela repintava
cada evento com o veredito agregado do objeto — mas herda todos os problemas acima: o ΔD
exibido continua confundido com luminância (§A1), e a régua de ±1σ é convenção, não
inferência.

Ela está corretamente documentada como **descritiva, não inferencial**. Vale manter essa
distinção visível na própria interface, não só no código.

---

## 12. Parte K — Reprodutibilidade e verificação

### K1. O plugin não tem nenhum teste automatizado

**Severidade: média.**

O repositório tem um *bench* substancial para o simulador Grasshopper (`grasshopper/verify`,
com 16 verificações de aceitação, varredura de sementes, teste de determinismo). O plugin —
onde está toda a estatística que sustenta a tese — **não tem nenhum**.

Nenhuma das funções puras é testada, e várias são trivialmente testáveis sem Revit:
`CalcularDesvioPadraoAmostral`, `CalcularPearson`, `ConverterEmPostos`,
`CalcularMedianaDeListaOrdenada`, `Classificar`, `CalcularPotencialHomeodinamico`,
`MapearFrequencia`, `ClassificarAtribuicao`, `PontuarBandaDupla`, todo o
`DetectorDeMovimentoOcular`.

**Por que é suspeito:** um erro de sinal em `CalcularPotencialHomeodinamico` inverteria
silenciosamente a leitura de todos os resultados — o próprio comentário do código diz isso.
Nada impediria esse erro de passar despercebido.

**Como melhorar:** um projeto xUnit com casos de referência calculados à mão. Prioridade,
em ordem: (1) `CalculadorDaCamada2` — o sinal e os limiares; (2)
`EstimadorDeEfeitoDeElemento` — centragem e `se` contra um exemplo numérico feito em
planilha; (3) `DetectorDeMovimentoOcular` — sequências sintéticas curtas com fixações e
zonas mortas conhecidas; (4) parsers, com uma linha de cada arquivo real.

### K2. Divergências texto ↔ código já conhecidas

Os `TODO(autor)` registram, com honestidade que merece nota:

- A **Camada 1 implementada não é a Equação 3** — é uma aproximação em dois estágios que
  omite o spline de gaze, os quatro regressores ambientais, o erro AR(1) e o ajuste misto
  por REML.
- O **Quadro 41 diverge da Equação 4** e remete a uma "Equação 5" que não existe no texto.
- O **Requisito 05 (§5.3) e a §5.5** ainda descrevem uma normalização min-max ponderada que
  **não existe mais** no código.
- A **agregação por participante** das Figuras 54–56 não está definida no texto.

**Como melhorar:** isso é dívida de *texto*, não de código, e é a mais urgente das dívidas —
um leitor da tese não tem como saber que o que está publicado não é o que roda. Uma seção de
"Implementação" descrevendo o que o código de fato faz, com as aproximações declaradas,
resolve.

---

## 13. Plano de ação priorizado

Ordenado por (impacto na validade) ÷ (esforço).

### Fazer primeiro — correções pequenas, efeito grande

| # | Ação | Onde | Esforço |
|---|---|---|---|
| ~~1~~ | ~~Ler `EyeOpened` como `double` e comparar com 0,5~~ | ✅ **feito em 2026-07-30** | — |
| 2 | Filtrar `Objeto sem nome` dos nomes de objeto da fixação | `DetectorDeMovimentoOcular.ExtrairNomesDeObjetos` | 15 min |
| 3 | Trocar 1,96 pelo quantil t com gl = n−1 | `CalculadorDaCamada2.Classificar` | 1 h |
| 4 | Usar DP **dentro** de participantes no denominador de `PH_e` | `EstimadorDeEfeitoDeElemento` | 30 min |
| 5 | Contar e reportar grupos descartados na zona morta 80–300 ms | `DetectorDeMovimentoOcular` | 1 h |
| 6 | Merge de sensores por tolerância temporal, não igualdade exata | `ConstruturDeSessoes` | 1 h |
| 7 | Exibir IC junto de `PH` e `ν̂ₑ` na tabela | `MetricasPorObjeto` + XAML | 3 h |

### Fazer em seguida — corrigem a inferência

| # | Ação | Esforço |
|---|---|---|
| 8 | Bootstrap por participante para o IC de `ν̂ₑ` (resolve §C3) | 1 dia |
| 9 | Benjamini-Hochberg sobre os elementos testados (resolve §D2) | meio dia |
| 10 | Limiar de estabilidade **angular** e detecção I-VT (resolve §A2, §A4) | 1–2 dias |
| 11 | Atribuição fracionária de fixações multi-objeto (resolve §C4) | meio dia |
| 12 | Completude mínima nos escores de formulário (resolve §G1) | 2 h |
| 13 | Δcortisol entre coletas do mesmo horário, ou AUCg (resolve §F1) | meio dia |
| 14 | Projeto de testes com as funções puras (resolve §K1) | 2 dias |

### Decisões de método — precisam de decisão sua, não de código

| # | Questão |
|---|---|
| 15 | **Luminância** (§A1): declarar como limitação, extrair do Revit como covariável, ou migrar para IPA/LHIPA? |
| 16 | **Condição de controle** (§C2): há como incluir uma cena neutra de referência? Sem ela, nenhuma afirmação absoluta é possível. |
| 17 | **Baseline** (§B1): tela cinza pré-cena, ou baseline local por fixação? |
| 18 | **Janela 2–4 min** (§B2): qual a justificativa? Rodar análise de sensibilidade. |
| 19 | **Agregação por participante** (§E1): corrigir conforme o TODO, ou aposentar as Figuras 54–56? |
| 20 | **Texto da tese** (§K2): escrever a seção de Implementação que descreve o que o código faz. |

---

## 14. O que está certo — para ser justo

Um documento só de críticas dá uma impressão errada da qualidade do código. O que está bem
feito e não deve ser mexido:

- **A mediana para a baseline**, e não a média — escolha robusta correta.
- **Spearman ao lado de Pearson**, com tratamento correto de empates por posto médio. A
  justificativa registrada no código (n = 26, variáveis não normais) está certa.
- **A separação Camada 1 / Camada 2** em classes puras e sem estado, testáveis
  isoladamente. A arquitetura está pronta para receber as correções acima.
- **`NaN` como "não estimável"**, distinto de zero, propagado com disciplina por todo o
  pipeline. É raro ver isso feito com consistência.
- **A distinção `Neutro` vs `SemPontuacao`** existe no domínio, mesmo que a interface ainda
  não a explore.
- **O sinal negativo dentro do `tanh`** está documentado como convenção fisiológica
  deliberada, com aviso explícito de que removê-lo inverteria tudo.
- **Os oito `TODO(autor)`** — nenhum deles é decorativo. Todos apontam divergências reais e
  várias das críticas deste documento apenas quantificam o que eles já diziam.
- **O bench do simulador Grasshopper**, que é de uma qualidade que o plugin merece ter.

---

## Apêndice A — Notas de rastreabilidade

Arquivos lidos integralmente ou em profundidade para este relatório:

```
Processamento/  CalculadorDaCamada2 · CalculadorDeCorrelacao · CalculadorDeEscoresDeFormulario
                CalculadorDeIndicadoresDeSbs · CalculadorDeMetricasPorObjeto
                CalculadorDePotencialPorParticipante · ConstruturDeSessoes
                DetectorDeMovimentoOcular · EstimadorDeEfeitoDeElemento
Dominio/        ClassificacaoDePotencialDeHitpoint · FaixaDeEstresseIeee · LinhaDesessao · Sessao
Configuracao/   Configuracoes
Parsing/        AnalisadorCsvSessao · AnalisadorExcelCortisol (parcial)
Interface/      JanelaPluginCAISM.xaml(.cs) — tabela de auditoria e de potencial
                Modelos/LinhaDeEventoParaTabela · Auditoria/ClassificadorDeEventoDaAuditoria
grasshopper/    README.md · GazeSessionSimulator.cs (constantes medidas dos arquivos reais)
```

Contas apresentadas neste documento e suas premissas:

- **Conversão 0,01 m → graus** (§A2): `θ = 2·atan(0,005/d)`.
- **Quantização de duração** (§A3, §A5): `duração = (n−1)·Δt`, com Δt = 59,2 ms a 16,9 Hz —
  taxa efetiva medida no bench do simulador.
- **Proporções de `EyeOpened`** (§B3): `SimulationDefaults.OpennessExactlyOneFraction = 0,77`
  e `OpennessNearOneFraction = 0,17`, medidas dos dois arquivos de captura reais.
- **Efeito de desenho** (§C3): `DEff = 1 + (m−1)ρ`, com ρ = 0,3 e m = 10 assumidos —
  **valores ilustrativos**; ρ deve ser estimado dos dados reais antes de citar o fator 1,92.
- **α real com quantil normal** (§D1): `P(|t_{n−1}| > 1,96)`, tabela padrão.
- **Falsos positivos esperados** (§D2): `α × E` sob hipótese nula completa.
- **Valor crítico de r** (§H1): `r_c = t_c/√(t_c² + n − 2)`, com `t_c` de gl = n − 2 = 24.

---

## Apêndice B — Anotação de manutenção

`MEMORY.md` (memória do assistente para este projeto) indexa um arquivo
`project_context.md` que **não existe** no diretório de memória. O índice tem quatro
entradas e só três arquivos. Vale recriar a nota de contexto do projeto ou remover a linha
órfã.
