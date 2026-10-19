# Referências verificadas: desenvolvimento e segurança de código

Conferidas em 19/10/2026 nas fontes primárias indicadas. Antes de citar num parecer, conferir de novo e dizer a data.

## 1. Exigências da AC Defesa sobre desenvolvimento (documentos do Projeto)

| Documento | Item | O que diz (resumo) |
|---|---|---|
| DPC da AC Defesa v4.0 (04/11/2024), `normas-AC-Defesa/DPC-ACDefesa-V4.0.pdf`, p. 66 | 6.6.1.1 | Sistema de certificação baseado em código aberto; customizações primeiro em desenvolvimento, depois homologação; implantação em produção decidida pelo Chefe da AC Principal |
| idem | 6.6.1.2 | Projeto e desenvolvimento com documentação suficiente para avaliação externa de segurança |
| idem | 6.6.2.1 | Verificação semanal dos níveis de segurança configurados, comparando com a configuração aprovada |
| idem | 6.6.2.2 | Gerenciamento formal de configuração para novas versões, novos procedimentos (inclusive módulos criptográficos) e novos serviços |
| idem | 6.6.3 | Controles de segurança de ciclo de vida: "Não se aplica" |
| idem | 6.7.1.1 b | Atualizações de SO e aplicativos implantadas logo após teste em desenvolvimento e homologação |
| Política de Segurança da AC Defesa v2.0 (12/06/2023), `PS-ACDefesa-V.2.0.pdf`, p. 16-17 | 9.3.1.1 a 9.3.1.5 | Segurança em cada etapa do ciclo de vida, documentação atualizada, controle de acesso, logs protegidos e analisados, verificação de integridade de dados e configuração, **testes de vulnerabilidade antes da produção** e avaliação periódica |

Observação: esses itens se aplicam aos sistemas da AC e das ARs. Um aplicativo de apoio desenvolvido pela OM (ex.: CifraToken) não está no escopo da DPC, mas a PS e a ISO 27002 servem de régua de boa prática. A DPC não trata de desenvolvimento de aplicativos de usuário: é omissa.

## 2. Plataforma .NET (Microsoft)

| Fato | Data / versão | Fonte |
|---|---|---|
| Fim de suporte do .NET 8 (LTS) e do .NET 9 (STS) | **10/11/2026** (pode haver uma última atualização nesse dia se houver falha crítica) | Microsoft .NET Blog, "\.NET 8 and .NET 9 will reach End of Support on November 10, 2026", Rahul Bhandari, 29/06/2026: https://devblogs.microsoft.com/dotnet/dotnet-8-9-end-of-support/ |
| Após o fim do suporte | Aplicações continuam rodando, mas sem correções de segurança nem suporte técnico | idem |
| .NET 10 (LTS) | suporte até nov/2028 (a postagem dá só o mês) | idem; política: https://learn.microsoft.com/en-us/dotnet/core/releases-and-support e https://learn.microsoft.com/en-us/lifecycle/products/microsoft-net-and-net-core |
| Versões STS passaram a ter 24 meses de suporte | — | https://devblogs.microsoft.com/dotnet/dotnet-sts-releases-supported-for-24-months/ |

Impacto para o Projeto: o CifraToken (pkrezende/normas_tecnicas, pasta cifra-token/) usa `net8.0` e `net8.0-windows` e o pacote System.Security.Cryptography.Pkcs 8.0.2 (conferido nos .csproj em 19/10/2026). Ficará sem correções de segurança a partir de 10/11/2026 se não migrar para .NET 10. Achado para a revisão dos especialistas 9 e 10; o código não foi alterado.

### Convenções de código C# (Microsoft Learn)
- Fonte: "Identifier names - rules and conventions", https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/identifier-names (consultado em 19/10/2026).
  - Tipos, métodos, propriedades e **constantes** (campos e locais, inclusive `private`): PascalCase.
  - Parâmetros e variáveis locais: camelCase. Campos privados de instância: `_camelCase`; estáticos privados: `s_`; `[ThreadStatic]`: `t_`.
  - Interfaces: prefixo `I` + PascalCase.
- Regras aplicáveis por `.editorconfig`: https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/style-rules/naming-rules
- Framework Design Guidelines (capitalização): https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/capitalization-conventions
- Diretrizes de codificação segura do .NET: https://learn.microsoft.com/en-us/dotnet/standard/security/secure-coding-guidelines (conferir conteúdo atual antes de citar).

## 3. OWASP

| Referência | Versão / data | Fonte |
|---|---|---|
| OWASP Top 10:2025 | edição 2025 (página não informa o dia de publicação; conferir antes de citar data) | https://top10.owasp.org/2025/ |
| OWASP ASVS (Application Security Verification Standard) | **5.0.0**, publicada em 30/05/2025, última estável em 19/10/2026 (há uma versão "bleeding edge" de 03/09/2026 só para teste) | https://github.com/OWASP/ASVS/releases |
| OWASP .NET Security Cheat Sheet | página viva, sem versão | https://cheatsheetseries.owasp.org/cheatsheets/DotNet_Security_Cheat_Sheet.html |

Top 10:2025:
A01 Broken Access Control; A02 Security Misconfiguration; A03 Software Supply Chain Failures; A04 Cryptographic Failures; A05 Injection; A06 Insecure Design; A07 Authentication Failures; A08 Software or Data Integrity Failures; A09 Security Logging and Alerting Failures; A10 Mishandling of Exceptional Conditions.

Pontos do .NET Security Cheat Sheet mais úteis para aplicativos desktop e de criptografia:
- Manter runtime e pacotes com suporte e atualizados.
- Não usar BinaryFormatter; preferir formatos só de dados (System.Text.Json) e validar o que for desserializado; desligar resolução de entidades externas em XML (XXE).
- AES-GCM: tamanho de tag explícito e nunca reutilizar nonce com a mesma chave.
- DPAPI: escolher o escopo com cuidado; segredos fora do código e dos arquivos de configuração.
- Comandos do sistema operacional: seguir o OS Command Injection Defense Cheat Sheet.
- Erros genéricos ao usuário, sem pilha de chamadas.
- Isolamento por permissões do SO; Code Access Security e confiança parcial **não** são fronteira de segurança.

## 4. CWE Top 25 (MITRE)

- Edição 2025, página atualizada em 15/12/2025: https://cwe.mitre.org/top25/archive/2025/2025_cwe_top25.html
- Mais relevantes para C# desktop e linha de comando: CWE-22 (path traversal, 6º), CWE-78 e CWE-77 (injeção de comando, 9º e 23º), CWE-502 (desserialização, 15º), CWE-20 (validação de entrada, 18º), CWE-200 (exposição de informação, 20º), CWE-306 (falta de autenticação em função crítica, 21º), CWE-770 (alocação sem limite, 25º; arquivos enormes).
- Lista completa: 1 CWE-79, 2 CWE-89, 3 CWE-352, 4 CWE-862, 5 CWE-787, 6 CWE-22, 7 CWE-416, 8 CWE-125, 9 CWE-78, 10 CWE-94, 11 CWE-120, 12 CWE-434, 13 CWE-476, 14 CWE-121, 15 CWE-502, 16 CWE-122, 17 CWE-863, 18 CWE-20, 19 CWE-284, 20 CWE-200, 21 CWE-306, 22 CWE-918, 23 CWE-77, 24 CWE-639, 25 CWE-770.

## 5. NIST SSDF (Secure Software Development Framework)

| Documento | Situação em 19/10/2026 | Fonte |
|---|---|---|
| NIST SP 800-218, SSDF v1.1 | final, fevereiro de 2022 | https://nvlpubs.nist.gov/nistpubs/specialpublications/nist.sp.800-218.pdf |
| NIST SP 800-218 Rev. 1 (SSDF v1.2) | **rascunho público** (initial public draft) desde 17/12/2025; nenhuma versão final anunciada (página de notícias atualizada em 06/10/2026) | https://csrc.nist.gov/projects/ssdf/news e https://csrc.nist.gov/pubs/sp/800/218/r1/ipd |

Grupos de práticas do SSDF v1.1: PO (preparar a organização), PS (proteger o software), PW (produzir software bem protegido), RV (responder a vulnerabilidades).

## 6. Pendente

- Normas de hardening em C# e de desempenho que o Pedro vai colocar no repositório (pasta `normas-desenvolvimento/`). Registrar aqui nome, versão, data e o que cobre quando chegarem.
