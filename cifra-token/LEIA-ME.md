# CifraToken

Aplicativo para Windows que cifra e decifra arquivos com o certificado digital ICP-Brasil guardado no
token (testado para o uso com o token Dexon / middleware DXSafe, gerenciado pelo Token Admin).

- O arquivo é cifrado com **AES-256-CBC** e a chave de sessão com o **RSA** de cada destinatário, no formato
  **CMS EnvelopedData** (RFC 5652, o mesmo do S/MIME). A saída é um `.p7m`.
- É **compatível com o `openssl cms`**: o que o CifraToken cifra o openssl decifra, e vice-versa
  (veja `pareceres/2026-10-19-teste-cifracao-linux.md` na biblioteca do projeto).
- Aceita **vários destinatários** no mesmo arquivo (por exemplo, você e o certificado de contingência do setor).
- A chave privada **não sai do token**: só a chave de sessão do arquivo é decifrada dentro dele.

> Não use para informação classificada. Para isso vale a solução de criptografia de Estado definida pelo seu
> Comando (Decreto 7.845/2012). Faça os testes com arquivos sem valor.

## Onde baixar

Na aba **Actions** do repositório, abra a execução mais recente do fluxo **CifraToken** e baixe o artefato
`CifraToken-windows`. Ele traz duas pastas:

| Pasta | Quando usar |
|---|---|
| `CifraToken-win-x64` | Windows 64 bits com o driver do token de 64 bits (o normal) |
| `CifraToken-win-x86` | Se o driver do token só existir em 32 bits (em `C:\Windows\SysWOW64`) |

Cada pasta tem `CifraToken.exe` (janela) e `cifratoken.exe` (linha de comando). Não precisa instalar o .NET.
Como o executável não é assinado, o Windows SmartScreen pode avisar na primeira execução ("Mais informações" > "Executar assim mesmo").

## Primeiro uso com o token Dexon

1. Instale o **DXSafe Middleware** da Dexon (o mesmo que acompanha o Token Admin) e conecte o token.
2. Abra o `CifraToken.exe`. No topo, em **Driver do token (PKCS#11)**, deve aparecer
   `C:\Windows\System32\DXSafePKCS11.dll`. Se não aparecer, clique em **Escolher...** e aponte para a DLL.
3. Clique em **Testar token**. A lista deve mostrar o seu certificado (não pede PIN).

Se a DLL tiver outro nome no seu computador, procure em `C:\Windows\System32` e `C:\Windows\SysWOW64`
arquivos com `pkcs11` no nome. O mesmo caminho é o que se configura no Quimera (Preferências > Credencial).

## Cifrar

1. Aba **Cifrar**: escolha (ou arraste) o arquivo.
2. **Do token / Windows...**: escolha o seu certificado e os de outros destinatários. Também dá para usar
   **De arquivo .cer...** com o certificado público de quem vai receber.
3. **Cifrar e salvar .p7m**. O original não é apagado.

O RSA-OAEP é opcional e só deve ser marcado se o token de todos os destinatários suportar.

## Decifrar

1. Aba **Decifrar**: escolha o `.p7m`. A janela mostra para quais certificados ele foi cifrado.
2. Escolha onde está a sua chave:
   - **Token pelo driver PKCS#11** (recomendado): o CifraToken pede o PIN e o token faz a operação.
     Se o token avisar que restam poucas tentativas, o aviso aparece antes do PIN.
   - **Token ou certificado publicado no Windows**: usa o certificado que o middleware publica em
     "Pessoal" (certmgr.msc); o próprio middleware pede o PIN.
   - **Arquivo A1 (.pfx)**.
3. **Decifrar e salvar**.

## Linha de comando

```
cifratoken listar --pkcs11                      # certificados do token (procura o DXSafePKCS11.dll)
cifratoken cifrar -i doc.pdf -o doc.pdf.p7m --do-token -r contingencia.cer
cifratoken decifrar -i doc.pdf.p7m -o doc.pdf --pkcs11
cifratoken destinatarios -i doc.pdf.p7m
cifratoken decifrar -i doc.pdf.p7m -o doc.pdf --pfx meu.pfx
```
O PIN é pedido na tela (ou lido de `CIFRATOKEN_PIN`, apenas para testes automatizados).

## Compatibilidade

| Com | Situação |
|---|---|
| `openssl cms` (Linux/Windows) | compatível nos dois sentidos, DER ou PEM, RSA PKCS#1 v1.5 e OAEP |
| Quimera (Marinha) `.acq` | **não** compatível: o Quimera usa formato próprio para cifrar para destinatários |
| Quimera `.enc` (senha) | não se aplica: o CifraToken não cifra com senha |

## Limites conhecidos

- O arquivo inteiro é carregado na memória (adequado para documentos; evite arquivos de vários GB).
- Um certificado renovado tem outra chave: arquivos cifrados para o certificado antigo só abrem com o antigo.
- Não apaga o original com segurança; faça isso pelo procedimento do seu setor, se for o caso.

## O que foi testado

Automaticamente, a cada alteração (GitHub Actions):
- interoperabilidade com o `openssl cms` (2 destinatários, DER e PEM, PKCS#1 e OAEP, chave errada recusada);
- token simulado (SoftHSM) por PKCS#11: listar, decifrar, cifrar com o certificado do token, PIN errado;
- no Windows: decifrar pelo repositório "Pessoal" e pelo caminho que o token usa.

**Ainda não testado com o token Dexon físico.** Esse teste precisa ser feito no seu computador.

## Código

- `src/CifraToken.Core`: CMS (`CmsCifra.cs`), acesso ao token por PKCS#11 (`TokenPkcs11.cs`, com a
  biblioteca [Pkcs11Interop](https://github.com/Pkcs11Interop/Pkcs11Interop)) e ao repositório do Windows.
- `src/CifraToken.App`: janela (Windows Forms, .NET 8).
- `src/CifraToken.Cli`: linha de comando.
- `tests/`: testes de unidade e `interop.sh` (openssl + SoftHSM).
