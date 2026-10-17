# Plugin CAISM — Análise de Estresse em Realidade Virtual

Plugin Revit para análise de dados de rastreamento ocular coletados em experimentos de Realidade Virtual no CAISM. Detecta fixações e saccades, calcula potencial restaurador por objeto e gera vistas 3D com família `hitpoint.rfa` parametrizada com Fator Restaurador.

---

## Estrutura de Pastas

```
PluginCAISM/
│
├── Configuracao/
│   └── Configuracoes.cs              # Limiares, pesos, constantes de conversão
│
├── Dominio/
│   ├── LinhaDeSessao.cs              # Uma amostra do CSV (linha bruta + sensores)
│   ├── EventoDeMovimentoOcular.cs    # Base abstrata + Fixacao + Saccade + enums
│   ├── Sessao.cs                     # Sessão processada (lazy fixações/saccades)
│   ├── ConjuntoDeSessoes.cs          # Container raiz, filtros, normalização
│   ├── MetricasPorObjeto.cs          # Métricas agregadas por objeto (gráficos)
│   └── RespostasDeFormularioPorParticipante.cs # Formulários (contextualização, §5.5.1)
│
├── Parsing/
│   ├── AnalisadorCsvSessao.cs        # Leitura do Input 01 (CSV de sessões)
│   ├── AnalisadorExcelSensor.cs      # Leitura do Input 02 (Excel de sensores)
│   ├── AnalisadorExcelCortisol.cs    # Leitura do Input 03 (Excel de cortisol)
│   └── AnalisadorExcelFormularios.cs # Leitura do Input 04 (Excel de formulários)
│
├── Processamento/
│   ├── DetectorDeMovimentoOcular.cs  # Detecção de fixações e saccades
│   ├── ConstruturDeSessoes.cs        # Agrupamento, enriquecimento, construção
│   ├── CalculadorDeMetricasPorObjeto.cs # Métricas de potencial restaurador
│   ├── CalculadorDeEscoresDeFormulario.cs # Escores IEEE / domínios A–E / SBS
│   └── CalculadorDePotencialPorParticipante.cs # Agrega o índice em um PH por indivíduo
│
├── Revit/
│   ├── ConversorDeCoordenadas.cs     # Unity (metros) → Revit (pés), mapeamento de eixos
│   ├── GerenciadorDeFases.cs         # Criação/localização de fases Revit por sessão
│   ├── InsersorDaFamiliaHitpoint.cs  # Carga e inserção de hitpoint.rfa
│   └── GeradorDeVistaRevit.cs        # Orquestração do fluxo completo de geração
│
├── Interface/
│   ├── JanelaPluginCAISM.xaml        # Layout WPF da janela principal
│   ├── JanelaPluginCAISM.xaml.cs     # Code-behind (sem lógica de dados)
│   ├── Modelos/
│   │   ├── LinhaDeEventoParaTabela.cs # ViewModel para a tabela de eventos
│   │   └── LinhaDeFormularioParaTabela.cs # ViewModel da tabela-síntese de formulários
│   └── Controles/
│       ├── ControleGraficoPorObjeto.cs # Gráfico de barras por objeto (Canvas)
│       └── ControleGraficoDeDispersao.cs # Dispersão escore × PH (LiveCharts)
│
├── ComandoPrincipalCAISM.cs          # IExternalCommand — entry point do Revit
├── PluginCAISM.csproj                # Projeto .NET 4.8
└── PluginCAISM.addin                 # Manifesto do addin para o Revit
```

---

## Pré-requisitos

- **Revit 2024** (ou ajustar os caminhos das DLLs no `.csproj`)
- **.NET Framework 4.8**
- **EPPlus 6** (instalado via NuGet automaticamente)
- Família **`hitpoint.rfa`** na mesma pasta do plugin, com parâmetro de instância `Double` chamado `Fator Restaurador`

---

## Instalação

1. Compilar o projeto em modo Release (x64).
2. Copiar `PluginCAISM.dll`, `EPPlus.dll` e `hitpoint.rfa` para a pasta de addins do Revit:
   ```
   %APPDATA%\Autodesk\Revit\Addins\2024\
   ```
3. Copiar `PluginCAISM.addin` para a mesma pasta.
4. Reiniciar o Revit.

---

## Uso

1. Abrir um projeto Revit (não família) em planta ou vista 3D.
2. Executar o comando `Plugin CAISM` via ribbon ou caixa de pesquisa.
3. Carregar os três arquivos de dados (sessões CSV, cortisol XLSX, sensores CSV).
4. Usar os filtros na aba **Análise do Grupo** para explorar os gráficos de potencial restaurador por objeto.
5. Clicar em **Gerar Vista Revit 3D** para criar fases, inserir hitpoints e abrir a vista gerada.

---

## Pendências antes da publicação

- Preencher os limiares em `Configuracao/Configuracoes.cs` com valores validados na literatura:
  - `DuracaoMinimaConfortavelDeFixacaoMs` e `DuracaoMaximaConfortavelDeFixacaoMs`
  - `PesoDaDuracaoDeFixacao` e `PesoDoDiametroPupilar`
- Confirmar o mapeamento de eixos Unity → Revit em `Revit/ConversorDeCoordenadas.cs`
  com base na orientação real do modelo exportado.
- Substituir o `ClientId` no `.addin` por um GUID único gerado para o projeto.
