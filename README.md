# YouTube Playlist Downloader Revamped

Baixe vídeos, playlists e canais inteiros do YouTube, em vídeo ou só o áudio, num app para Windows com interface moderna, tema claro e escuro e 14 idiomas.

Este projeto é um fork com interface e fluxo de uso totalmente refeitos do [YouTube Playlist Downloader](https://github.com/shaked6540/YoutubePlaylistDownloader) original. Os dois são independentes: use o que preferir.

![Canal carregado no tema escuro](docs/screenshots/canal-escuro.png)

| Tela inicial | Tema claro |
| --- | --- |
| ![Tela inicial](docs/screenshots/inicio.png) | ![Canal carregado no tema claro](docs/screenshots/canal-claro.png) |

## Recursos

- **Cole e pronto:** aceita link de vídeo, playlist ou canal (`youtube.com/@nome`). Cole vários links, um por linha, para baixar tudo de uma vez.
- **Escolha o que baixar:** veja os vídeos da playlist em grade ou lista, filtre, busque e marque só os que quiser. O app avisa quais já existem na pasta.
- **Vídeo ou só áudio:** MP4 ou MKV até 8K, ou áudio em MP3, M4A, AAC, OPUS, FLAC, WAV, OGG e WEBM. Também dá para salvar o áudio original, sem conversão.
- **Fila de downloads:** vários downloads ao mesmo tempo, com progresso, velocidade e tempo restante. Dá para pausar, retomar, cancelar e tentar de novo.
- **Tags automáticas:** título, artista, álbum e capa nos arquivos de áudio.
- **Nome dos arquivos do seu jeito:** padrões como `$index - $title`, com prévia ao vivo.
- **Legendas e faixa de áudio:** baixa legendas e escolhe o idioma do áudio em vídeos dublados.
- **Detalhes que ajudam:** detecta links copiados, aceita links arrastados do navegador, mostra notificação ao terminar e o progresso na barra de tarefas.
- **14 idiomas:** português (Brasil), English, Español, Deutsch, Français, Italiano, Nederlands, Polski, Română, Türkçe, Русский, עברית, العربية e 中文.

## Instalação

1. Baixe o instalador mais recente na página de [Releases](https://github.com/danfreitas97/YoutubePlaylistDownloaderRevamped/releases).
2. Execute o `YoutubePlaylistDownloaderRevamped-Setup.exe`.

O app precisa do Windows 10 ou 11 e do [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0). Ele pode ser instalado ao lado do app original, sem conflito.

## Como usar

1. Copie o link no YouTube.
2. Abra o app: ele sugere o link copiado. Se não sugerir, cole no campo **Link do YouTube** e clique em **Buscar vídeos**.
3. Marque os vídeos que quiser, escolha **Vídeo** ou **Só áudio**, a qualidade, o formato e a pasta.
4. Clique em **Baixar** e acompanhe tudo na tela **Downloads**.

Na primeira vez que você baixar um vídeo ou converter um áudio, o app vai pedir para baixar o **FFmpeg**, um programa gratuito que junta vídeo e áudio e converte as músicas. É só clicar em **Baixar**: são cerca de 75 MB, uma vez só, e depois o seu download continua sozinho.

## "O YouTube está bloqueando as requisições"

Às vezes o YouTube limita temporariamente o acesso anônimo de uma conexão (erro 429). Quando isso acontecer, clique em **Usar minha conta do YouTube** no próprio aviso, ou vá em **Configurações › Conta do YouTube › Entrar com o YouTube**.

Abre uma janela com a tela de login do Google. Entre normalmente e a janela fecha sozinha quando terminar. O app guarda só a sessão do YouTube, no seu computador, em `%AppData%\YouTube Playlist Downloader Revamped`. Sua senha nunca passa pelo app. Para desconectar, clique em **Remover** na mesma tela.

Se a janela de login não abrir (ela usa o Microsoft Edge WebView2, que já vem no Windows 10 e 11), dá para importar um `cookies.txt` exportado do navegador com uma extensão como a **Get cookies.txt LOCALLY**: **Configurações › Conta do YouTube › Ou importe um arquivo cookies.txt**.

## Problemas e sugestões

Encontrou um bug ou tem uma ideia? [Abra uma issue](https://github.com/danfreitas97/YoutubePlaylistDownloaderRevamped/issues) contando o que aconteceu. Se puder, anexe o log de erros: **Configurações › Avançado › Abrir pasta**, arquivo `Errors.txt`. Toda issue é avaliada e corrigida.

## Compilando o código

Requisitos: Windows e [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```
git clone https://github.com/danfreitas97/YoutubePlaylistDownloaderRevamped.git
cd YoutubePlaylistDownloaderRevamped
dotnet build YoutubePlaylistDownloader/YoutubePlaylistDownloader.csproj -c Release
```

Para baixar vídeo e converter áudio, o app usa o [FFmpeg](https://ffmpeg.org/). Ele procura o `ffmpeg.exe` ao lado do executável, depois em `%AppData%\YouTube Playlist Downloader Revamped` e por fim no `PATH`. Se não achar, oferece baixar uma versão pronta do [FFmpegBin](https://github.com/Tyrrrz/FFmpegBin) para a pasta do app.

Tecnologias: WPF (.NET 8), [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet), [YoutubeExplode](https://github.com/Tyrrrz/YoutubeExplode), [TagLib#](https://github.com/mono/taglib-sharp), [VirtualizingWrapPanel](https://github.com/sbaeumlisberger/VirtualizingWrapPanel) e [WebView2](https://learn.microsoft.com/microsoft-edge/webview2/).

## Aviso

Este app não é afiliado ao YouTube nem ao Google. Baixar vídeos pode ir contra os termos de serviço do YouTube. Use com responsabilidade e respeite os direitos autorais de quem cria o conteúdo.

## Licença

Distribuído sob a [Licença Apache 2.0](LICENSE), a mesma do projeto original. Este fork modifica o código original: a interface foi reescrita e a lógica de download foi reorganizada. As licenças das bibliotecas usadas estão em [License.txt](YoutubePlaylistDownloader/License.txt).

## Créditos

Este projeto existe graças ao [YouTube Playlist Downloader](https://github.com/shaked6540/YoutubePlaylistDownloader), criado por [shaked6540](https://github.com/shaked6540), que é a base deste fork. Agradecimento também às pessoas que traduziram o app original: Dong Cunrui (董存瑞), RichyZ99, Jhonatan Lima (Heremitus), İskender Alpsakarya e Alp Eren Gençoğlu (Türkçe), Stephan Komov (Deutsch), Elta e Zebre2007-2 (Français), Ahmed Waaer (العربية), Bartlomiej Szmolke (Polski), Timofey Dedov (Русский) e deeaeu (Română).
