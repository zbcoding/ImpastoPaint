# Impasto

[🇬🇧 English](readme.md) · Tradução automática — correções são bem-vindas por meio de um pull request.

O Impasto é um aplicativo de pintura e edição de imagens para Linux, Windows e macOS.

O Impasto é ótimo para pinturas rápidas, edições de imagens, recortes, redimensionamentos e edição baseada em camadas.
Use as ferramentas de texto para escrever textos, as ferramentas de forma para desenhar linhas livres e as ferramentas de seleção para copiar e colar partes de uma camada da imagem.
Veja as capturas de tela abaixo.

![Formas e textos como objetos editáveis, com histórico por objeto](docs/screenshots/impasto-object-layers.png)

![A ferramenta de texto, com a caixa de diálogo de preferências da interface aberta](docs/screenshots/impasto-text-tool.png)

![Um ponto de controle de forma mantido na linha central da tela de pintura, com a guia desenhada durante o arraste](docs/screenshots/impasto-snap-to-grid.png)
Imagem do recurso: o que você move e o que você desenha se ajusta à grade da tela de pintura, às unidades da régua ou às bordas e linhas centrais da tela de pintura.

## Download

As compilações para Linux, Windows e macOS estão na
[página de versões](https://github.com/zbcoding/ImpastoPaint/releases/latest). As compilações para Windows e
macOS não são assinadas, portanto o SmartScreen e o Gatekeeper exibirão avisos sobre elas.

O Impasto precisa do GTK 4.18 ou mais recente, o que determina o que funciona no Linux:

- **Flatpak**, disponível no [FlatPark](https://flatpark.org/apps/com.github.zbcoding.Impasto/) ou na
  página de versões. Ele traz o próprio GTK e roda em qualquer lugar em que o flatpak rode, incluindo Ubuntu 22.04
  e 24.04. É esta a opção a usar se você estiver em dúvida.
- **AppImage** (`Impasto-x86_64.AppImage`). Inclui o GTK, então nenhum GTK do sistema é necessário, mas o
  pacote é compilado com a **glibc 2.43** e não inicia em nada mais antigo. Isso significa que
  Ubuntu 26.04, Fedora 44 e Arch funcionam; Ubuntu 24.04, Ubuntu 22.04, Fedora 43 e Debian 13
  não funcionam - use o Flatpak nesses casos.
- **Zip** (`Impasto-linux-dotnet-*.zip`). Usa o GTK fornecido pela sua distribuição, portanto exige o
  GTK 4.18+ já instalado.

O Impasto é licenciado sob a Licença MIT (veja `license-mit.txt`). As atribuições, os
avisos e os textos de licença de terceiros estão em `THIRD-PARTY-NOTICES.md`.

## Relação com o Pinta

O Impasto é um projeto novo e separado. Ele começou a partir do código-fonte licenciado sob MIT do
[Pinta](https://github.com/PintaProject/Pinta) — um aplicativo GTK que, por sua vez, foi inspirado no
[Paint.NET™](https://www.getpaint.net/)
Os colaboradores do Pinta estão listados no aplicativo Impasto.
O Impasto não é mantido pelo projeto Pinta nem pelos mesmos colaboradores.

<details>
<summary><h2>Compilando no Windows</h2></summary>

Primeiro, instale as dependências relacionadas ao GTK que são necessárias:
- Instale o [MSYS2](https://www.msys2.org)
- No terminal CLANG64, execute `pacman -S mingw-w64-clang-x86_64-libadwaita mingw-w64-clang-x86_64-webp-pixbuf-loader`.
  - No Windows ARM64, use o terminal `CLANGARM64` e substitua `clang-x86_64` por `clang-aarch64`.

Em seguida, o aplicativo pode ser compilado abrindo o `Pinta.sln` no [Visual Studio](https://visualstudio.microsoft.com/).
Verifique se o .NET 10 está instalado por meio do instalador do Visual Studio.

Para compilar pela linha de comando:
- [Instale o SDK do .NET 10](https://dotnet.microsoft.com/).
- Compilar:
  - `dotnet build`
- Executar:
  - `dotnet run --project Impasto`

</details>

<details>
<summary><h2>Compilando no macOS</h2></summary>

- Instale o .NET 10 e o GTK4
  - `brew install dotnet-sdk libadwaita adwaita-icon-theme gettext webp-pixbuf-loader`
  - No Apple Silicon, defina `DYLD_LIBRARY_PATH=/opt/homebrew/lib` no ambiente para que o aplicativo consiga carregar as bibliotecas do GTK
  - No Intel, defina `DYLD_LIBRARY_PATH=/usr/local/lib` no ambiente para que o aplicativo consiga carregar as bibliotecas do GTK
- Compilar:
  - `dotnet build`
- Executar:
  - `dotnet run --project Impasto`

</details>

<details>
<summary><h2>Compilando no Linux</h2></summary>

- Instale o [.NET 10](https://dotnet.microsoft.com/) seguindo as instruções da sua distribuição Linux.
- Instale as outras dependências (as instruções são para o Ubuntu 22.10, mas devem ser semelhantes em outras distribuições):
  - `sudo apt install autotools-dev autoconf-archive gettext intltool libadwaita-1-dev`
  - Versões mínimas das bibliotecas: `gtk` >= 4.18 e `libadwaita` >= 1.8
  - Dependências opcionais: `webp-pixbuf-loader`
- Compilar (opção 1, para desenvolvimento e testes):
  - `dotnet build`
  - `dotnet run --project Impasto`
- Compilar (opção 2, para instalação):
  - `./autogen.sh`
    - Se estiver compilando a partir de um tarball, execute `./configure` em vez disso.
    - Adicione o argumento `--prefix=<diretório de instalação>` para instalar em um diretório diferente de `/usr/local`.
  - `make install`

</details>

## Obtendo ajuda / contribuindo:

Contribuições são bem-vindas. Em resumo:

- **Código** — a maneira mais rápida de contribuir é abrir uma issue descrevendo sua sugestão
  ou solicitação. Inclua trechos de código, nomes de arquivos e contexto.
  Você também pode enviar um PR. Contribuições recebidas como PR para a edição gratuita
  também podem ser usadas na edição Premium do Impasto ou em qualquer outro software que tenha a licença MIT.
- **Ferramentas de programação com IA** — são bem-vindas. Saiba explicar o código que você envia e
  adaptá-lo à arquitetura do projeto; código escrito com auxílio de IA pode receber uma
  revisão adicional antes de ser integrado.
- **Traduções** — contribuídas como pull requests: rascunhadas por IA em novos arquivos
  `.po` e depois editadas e revisadas por um falante do idioma.

Antes de contribuir, leia o guia completo, incluindo o fluxo de trabalho com git/PR, em `CONTRIBUTING.md`.

- Você pode relatar [bugs/problemas](https://github.com/zbcoding/ImpastoPaint/issues).
- As mudanças relevantes de cada versão são registradas no `CHANGELOG.md`.
