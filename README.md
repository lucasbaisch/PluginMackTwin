# MackTwin — painel de sensores por ambiente para o Revit 2026

Add-in do Autodesk Revit 2026 (C# / .NET 8 / WPF) que mostra, sala por sala, as leituras dos
sensores instalados no CCT Mackenzie Campinas. Ao clicar numa sala no modelo, o painel exibe os
dados de todos os sensores daquela sala em três visualizações: **cartões ao vivo**, **gráficos**
e **a sala pintada no próprio modelo** pela faixa de conforto. A aba Modelo também exporta o
**vídeo do último dia** (a vista 3D de conforto a cada hora das últimas 24 h, como o estudo solar
nativo — GIF animado + quadros PNG em `Documentos\MackTwin`) e gera a **folha para impressão**
(uma por nível: plantas de temperatura e de umidade lado a lado, cada uma com a sua legenda).
Clicar num Room no modelo seleciona a sala no painel.

Universidade Presbiteriana Mackenzie / Limiar.

## Como funciona

1. Cada sensor (hoje, um DHT22 de temperatura e umidade) grava numa **planilha Google pública**.
   O plug-in baixa o CSV de exportação a cada 60 s — sem API nem credencial.
2. O **HOSTNAME** do sensor codifica a sala: `MMCCTLAB001-001` = Mackenzie / CCT / sala
   `LAB001` / dispositivo `001`. É isso que casa o sensor com o **Room** do modelo, pelo número
   (ou nome) do Room, sem cadastro manual. Um sensor novo que siga o padrão aparece sozinho na
   sala certa.
3. As faixas de conforto seguem a ABNT NBR 17037:2023 (21–26 °C, 35–65 % UR). A cor da sala é
   a da **última** leitura; a janela de tempo (1 h, 6 h, 24 h, 7 dias, tudo) vale para as três
   visualizações.

Outras planilhas (outros sensores, outras pastas) entram pela própria interface
(**Planilhas › Adicionar…**); o cadastro fica em `%APPDATA%\MackTwin\planilhas.json`.

## Estrutura

```
MackTwin.slnx
src/MackTwin/                 projeto do add-in (MackTwin.csproj → MackTwin.dll)
  Configuracao/               faixas de conforto, convenção da planilha, ribbon, cadastro
  Dominio/                    Sensor, Leitura, Ambiente, ResumoDoAmbiente, CodigoDeSala
  Parsing/                    LeitorDePlanilhaGoogle, AnalisadorDeHostname, UtilitariosDeCsv
  Processamento/              ClassificadorDeConforto, resumo por sala, agrupamento, monitor
  Revit/                      despachante (ExternalEvent), casamento com Rooms, pintura, vista 3D, vídeo, folha
  Exportacao/                 linha do tempo do vídeo, carimbo dos quadros, codificador de GIF animado
  Interface/                  janela WPF modeless, cartões, gráficos, cadastro de planilha
  Verificacao/                asserções automáticas (botão "Verificar modelo")
  Assets/                     logos e ícone
Installer/                    MSI (WiX v5) — ver Installer/README.md
build-installer.ps1           gera o MSI pela linha de comando
```

Linha de montagem: **Parsing** lê a planilha → **Processamento** agrupa em salas e resume →
**Revit** casa com os Rooms e pinta → **Interface** exibe. **Dominio** e **Configuracao** dão
suporte; **Verificacao** confere as partes puras.

## Compilar

Requisitos: Visual Studio 2026 (ou MSBuild) com .NET 8 e Revit 2026 instalado em
`C:\Program Files\Autodesk\Revit 2026\`.

```
MSBuild src\MackTwin\MackTwin.csproj -restore -t:Rebuild -p:Configuration=Debug
```

O build copia a saída para `%APPDATA%\Autodesk\Revit\Addins\2026\MackTwin\`. Com o Revit aberto
a cópia é pulada com aviso; feche o Revit para carregar a versão nova.

## Carregar no Revit

- **Desenvolvimento:** AddinManager → `MackTwin.dll` → `MackTwin.ComandoPrincipalMackTwin`
  (painel) ou `MackTwin.ComandoDeVerificacaoMackTwin` (asserções).
- **Instalação:** compile em **Release** ou rode `.\build-installer.ps1`; o MSI instala o
  manifesto e cria a aba **MackTwin › Sensores** na ribbon.

## Verificar

O botão **Verificar modelo** roda, dentro do Revit e sem rede, as asserções sobre a análise do
CSV (vírgula decimal, carimbo, duplicatas), a URL de exportação, a extração da sala a partir do
HOSTNAME, o casamento sala↔sensor, as faixas de conforto e o resumo por ambiente.
