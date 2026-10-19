# CifraToken

Aplicativo para Windows e Linux que cifra e decifra arquivos com o certificado digital ICP-Brasil guardado
no token de qualquer fabricante (Dexon, G+D SafeSign, SafeNet e outros, pelo driver PKCS#11), num arquivo
.pfx ou com frase-senha. Usa só algoritmos abertos.

- O arquivo é cifrado com **AES-256-CBC** e a chave de sessão com o **RSA** de cada destinatário, no formato
  **CMS EnvelopedData** (RFC 5652, o mesmo do S/MIME). A saída é um `.p7m`.
- É **compatível com o `openssl cms`**: o que o CifraToken cifra o openssl decifra, e vice-versa
  (veja `pareceres/2026-10-19-teste-cifracao-linux.md` na biblioteca do projeto).
- Aceita **vários destinatários** no mesmo arquivo (por exemplo, você e o certificado de contingência do setor).
- A chave privada **não sai do token**: só a chave de sessão do arquivo é decifrada dentro dele.

> Não use para informação classificada. Para isso vale a solução de criptografia de Estado definida pelo seu
> Comando (Decreto 7.845/2012). Faça os testes com arquivos sem valor.

## Onde baixar

Em **Releases** no repositório (coluna da direita), na versão mais recente, em **Assets**:

| Arquivo | Quando usar |
|---|---|
| `CifraToken-win-x64.zip` | Windows 64 bits com driver do token de 64 bits (o normal) |
| `CifraToken-win-x86.zip` | Windows, se o driver do token só existir em 32 bits (em `C:\Windows\SysWOW64`) |
| `CifraToken-linux-x64.tar.gz` | Linux 64 bits |

Cada pacote tem `CifraToken` (janela) e `cifratoken` (linha de comando). Não precisa instalar o .NET.
No Windows, o SmartScreen pode avisar na primeira execução ("Mais informações" > "Executar assim mesmo").

## Telas

A interface segue o fluxo do Quimera: barra de comandos à esquerda (o botão de menu recolhe e expande),
lista de arquivos à esquerda e opções à direita, com **Ok** e **Cancelar** embaixo.

![Cifrar](docs/telas/cifrar.png)

## Primeiro uso

1. Instale o middleware do seu token (Dexon DXSafe, G+D SafeSign, SafeNet, Watchdata etc.) e conecte o token.
2. Abra o CifraToken > **Preferências** > **Credencial**. Em "Driver para acesso ao token", escolha o driver
   na lista dos encontrados (ou **Procurar...** para indicar outra DLL) e clique em **Testar a conexão**
   (lê os certificados sem pedir PIN). No Windows também dá para usar o **repositório de certificados do Windows**,
   e em qualquer sistema um **arquivo PFX** (A1).
3. Em **Contatos e Certificados**, clique em **Adicionar meu certificado** e importe o `.cer` de quem vai
   receber arquivos (por exemplo, o certificado de contingência do setor). Use **Exportar .cer** para mandar o
   seu certificado público a quem vai cifrar para você.

| Driver | Arquivo |
|---|---|
| Dexon DXSafe | `DXSafePKCS11.dll` |
| G+D SafeSign | `aetpkss1.dll` |
| SafeNet / Thales eToken | `eTPKCS11.dll` |
| Watchdata | `WDPKCS.dll` |
| Linux (OpenSC e outros) | `opensc-pkcs11.so` ou o `.so` do fabricante |

## Cifrar Arquivos

Adicione os arquivos (botão **+** ou arrastando). Com as setas da faixa cinza, escolha:
- **Cifrar para Destinatários**: marque os contatos (inclua você mesmo). Gera `ARQUIVO.p7m`.
- **Cifrar com Senha**: frase-senha forte (14+ caracteres). Gera `ARQUIVO.enc`, que abre no Quimera e no openssl.

Os cifrados são salvos ao lado dos originais, que não são apagados.

## Decifrar Arquivos

Adicione os `.p7m` e `.enc`. Para os `.enc`, digite a senha. Para os `.p7m`, digite o PIN e clique em
**Conectar** (se o token avisar que restam poucas tentativas, o aviso aparece antes). Clique em **Ok**:
os decifrados são salvos na mesma pasta, com o nome original. Por padrão a sessão do token é encerrada
após cada uso (Preferências).

## Linha de comando

```
cifratoken listar --pkcs11                      # certificados do token (procura o DXSafePKCS11.dll)
cifratoken cifrar -i doc.pdf -o doc.pdf.p7m --do-token -r contingencia.cer
cifratoken decifrar -i doc.pdf.p7m -o doc.pdf --pkcs11
cifratoken destinatarios -i doc.pdf.p7m
cifratoken decifrar -i doc.pdf.p7m -o doc.pdf --pfx meu.pfx
cifratoken cifrar-senha -i doc.pdf -o doc.pdf.enc       # formato .enc do Quimera / openssl enc
cifratoken decifrar-senha -i doc.pdf.enc -o doc.pdf
```
O PIN é pedido na tela (ou lido de `CIFRATOKEN_PIN`, apenas para testes automatizados).

## Compatibilidade

| Com | Situação |
|---|---|
| `openssl cms` (Linux/Windows) | compatível nos dois sentidos, DER ou PEM, RSA PKCS#1 v1.5 e OAEP |
| `openssl enc` e Quimera `.enc` (senha) | compatível nos dois sentidos (AES-256-CBC, PBKDF2-SHA512, 1.000.000 de iterações) |
| Quimera (Marinha) `.acq` | **não** compatível: formato próprio do Quimera para destinatários |

## Limites conhecidos

- O arquivo inteiro é carregado na memória (adequado para documentos; evite arquivos de vários GB).
- Um certificado renovado tem outra chave: arquivos cifrados para o certificado antigo só abrem com o antigo.
- Não apaga o original com segurança; faça isso pelo procedimento do seu setor, se for o caso.

## O que foi testado

Automaticamente, a cada alteração (GitHub Actions):
- interoperabilidade com o `openssl cms` (2 destinatários, DER e PEM, PKCS#1 e OAEP, chave errada recusada);
- cifra com senha contra o `openssl enc` com os parâmetros do Quimera;
- token simulado (SoftHSM) por PKCS#11: listar, decifrar, cifrar com o certificado do token, PIN errado;
- no Windows: decifrar pelo repositório "Pessoal" e pelo caminho que o token usa.

**Ainda não testado com tokens físicos (Dexon, SafeSign).** Esse teste precisa ser feito no seu computador.

## Código

- `src/CifraToken.Core`: CMS (`CmsCifra.cs`), senha (`SenhaOpenSsl.cs`), contatos, acesso ao token por PKCS#11 (`TokenPkcs11.cs`, com a
  biblioteca [Pkcs11Interop](https://github.com/Pkcs11Interop/Pkcs11Interop)) e ao repositório do Windows.
- `src/CifraToken.App`: janela (Avalonia, roda no Windows e no Linux).
- `src/CifraToken.Cli`: linha de comando.
- `tests/`: testes de unidade e `interop.sh` (openssl + SoftHSM).
