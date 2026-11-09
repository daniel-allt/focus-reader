# Focus Reader

Aplicativo para Windows (C# / WinForms, .NET 8) que ajuda na leitura: ao ativar o **modo de leitura**, o cursor vira a mão com o indicador apontando e um **realce transparente** acompanha o cursor logo acima da ponta do dedo. Basta passar o realce sobre a linha que está lendo para manter o foco somente naquelas palavras.

## Funcionalidades

- Fica na **bandeja do sistema** (system tray), sem janela na barra de tarefas.
- Menu de contexto do ícone: **Ativar/Desativar modo de leitura**, **Configurações...** e **Sair**. Duplo clique no ícone abre as configurações.
- **Atalho global** (padrão `Ctrl + Alt + R`) para ativar/desativar o modo de leitura de qualquer programa.
- **Estabilização vertical**: pequenos desvios verticais do mouse não tiram o realce da linha.
- Realce **click-through**: não atrapalha cliques, seleção de texto nem rolagem.
- Ajusta o tamanho do realce ao DPI de cada monitor.
- O cursor original do usuário é restaurado ao desativar, ao sair, ao encerrar a sessão do Windows e na próxima execução caso o app tenha sido finalizado de forma inesperada.

## Configurações

| Opção | Descrição |
| --- | --- |
| Cursor ao ativar | Mão (padrão), seta, cruz, seta para cima, I-beam, ajuda, arquivo `.cur`/`.ani` personalizado ou não alterar |
| Cor do realce | Qualquer cor (seletor de cores do Windows) |
| Opacidade | 5% a 95% (o restante é transparência) |
| Tamanho do realce | Largura x altura em pixels (escalado pelo DPI) |
| Distância do cursor | Espaço entre a ponta do cursor e a borda inferior do realce |
| Estabilização vertical | O realce fica preso na linha e segue o mouse só na horizontal até o cursor se afastar mais que a tolerância (padrão 18 px); então desliza suavemente para a nova linha |
| Atalho global | Qualquer combinação com Ctrl/Alt/Shift/Win, ou F1–F24 sozinhas |

As configurações ficam em `%APPDATA%\FocusReader\settings.json`.

## Compilar e executar

Requer o [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
dotnet run --project src/FocusReader
```

Gerar um executável único:

```powershell
# Requer o .NET 8 Desktop Runtime instalado na máquina
dotnet publish src/FocusReader -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish

# Ou totalmente autocontido (não requer runtime)
dotnet publish src/FocusReader -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o publish
```

O GitHub Actions compila a cada push/PR e disponibiliza os dois executáveis como artefatos. Ao criar uma tag `v*` os executáveis são anexados a um release.

## Estrutura

```
src/FocusReader/
  Program.cs                  ponto de entrada (instância única, tratamento de erros)
  TrayApplicationContext.cs   ícone da bandeja, menu, ativação do modo de leitura
  AppSettings.cs              modelo e persistência das configurações (JSON)
  GlobalHotkey.cs             atalho global (RegisterHotKey)
  SystemCursorManager.cs      troca/restauração do cursor do sistema
  Forms/OverlayForm.cs        janela de realce transparente que segue o cursor
  Forms/SettingsForm.cs       tela de configurações
  Forms/HotkeyTextBox.cs      campo para capturar o atalho
  Forms/PreviewPanel.cs       pré-visualização do realce
```
