# Instalador (MSI) do Plugin CAISM

Empacota o plugin num `.msi` que instala para **todos os usuários** da máquina.

## Como gerar

**Pelo Visual Studio:** troque a configuração para **Release** e compile (F6).
O MSI sai em `Installer\bin\Release\PluginCAISM-Setup.msi`, e o caminho aparece
na janela de Saída.

**Pela linha de comando**, o equivalente:

```
.\build-installer.ps1
```

### Como o VS consegue, se não carrega .wixproj

Não carrega mesmo — o VS ignora projetos `.wixproj` em silêncio sem a extensão
HeatWave (FireGiant), e por isso o instalador **não está no `PluginCAISM.slnx`**.

Quem chama o WiX é o alvo `GerarInstaladorMsi`, no `PluginCAISM.csproj`: depois
de compilar em Release, ele dispara um `dotnet build` do `Installer.wixproj` como
processo separado. O MSBuild de fora resolve o WiX via NuGet e não depende de
extensão nenhuma.

O alvo passa `-p:BuildProjectReferences=false` porque o `Installer.wixproj`
referencia o próprio `PluginCAISM.csproj`: sem a flag, o build de dentro
recompilaria o plug-in e dispararia o alvo de novo, em recursão.

## Efeito sobre o desenvolvimento com AddinManager

Nenhum. O alvo só roda em **Release**; compilar em Debug continua levando ~4 s e
não empacota nada.

## O que o MSI instala

```
%ProgramData%\Autodesk\Revit\Addins\2026\
    PluginCAISM.addin          <- manifesto que o Revit lê na inicialização
    PluginCAISM\
        PluginCAISM.dll
        EPPlus.dll, LiveCharts*.dll, Microsoft.Extensions.*.dll, ...
        Assets\hitpoint.rfa
```

`Package.wxs` empacota a pasta de saída inteira via `<Files Include="...\**">`.
Acrescentar um `PackageReference` no csproj **não** exige editar nada aqui.

## Onde o plugin aparece no Revit

Aba própria **CAISM** → painel **Análise de Estresse** → botão **Análise CAISM**.

Quem monta isso é `AplicacaoRibbonCAISM`, registrada no `.addin` como
`Type="Application"`. O Revit só chama `OnStartup` para addins carregados pelo
manifesto — **o AddinManager não passa por lá**, então a aba não aparece no
desenvolvimento, só depois de instalar o MSI. Para testar a ribbon é preciso
instalar e reabrir o Revit.

## A cada nova versão

Suba `Version` no `Package.wxs` para que a versão apareça corretamente em
Programas e Recursos. Não mexa em `UpgradeCode` (é o que liga uma versão à
anterior) nem no `AddInId` do `.addin` (é a identidade do addin para o Revit).

Para **iterar** não é preciso subir nada: `AllowSameVersionUpgrades="yes"` faz o
instalador substituir uma instalação de mesma versão. Sem essa flag o MSI não
atualiza nada ao reinstalar — ele registra mais um produto ao lado do anterior e
os arquivos antigos continuam em disco, porque o WiX gera um ProductCode novo a
cada build enquanto o `Version` segue fixo.

Se desconfiar que o Revit está carregando uma versão antiga, compare a data do
DLL instalado com a do build:

```
%ProgramData%\Autodesk\Revit\Addins\2026\PluginCAISM\PluginCAISM.dll
```

## Limitações conhecidas

- **Só Revit 2026.** Outra versão exige um `.addin` na pasta do ano correspondente;
  Revit 2024 e anteriores usam .NET Framework 4.8 e precisariam de outro build.
- **Não verifica se o Revit está instalado.** O MSI copia os arquivos de qualquer jeito.
- **MSI sem assinatura digital.** O SmartScreen alerta ao baixar. Aceitável para
  distribuição interna; remover o alerta exige certificado de assinatura de código.
- **WiX v5**, e não v6/v7: a partir da v6 o WiX exige aceitar a EULA do
  Open Source Maintenance Fee. A v5 é livre e cobre tudo que é usado aqui.
