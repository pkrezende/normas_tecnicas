# Fontes indicadas pelo Pedro (19/10/2026)

Avaliação feita em 19/10/2026 pelo conselho (especialistas 9 e 10). Nenhuma das três é norma: servem de apoio e de modelo. Para fundamentar parecer, usar as fontes primárias de referencias-verificadas.md e a ISO/IEC 27002:2022.

## 1. ISO 27001 Compliance Document Templates

- Endereço: https://github.com/umarmusman/ISO27001_Compliance_Document_Templates (último commit em 14/10/2026, conferido em 19/10/2026).
- O que é: 16 documentos (.docx e um .pdf) de prontidão para ISO/IEC 27001:2022 de uma empresa fictícia de tecnologia financeira ("ABC-FinTech"). O README declara que os documentos e cenários foram gerados com IA.
- Licença: o repositório **não tem arquivo de licença** (conferido no clone). Sem licença, o padrão é "todos os direitos reservados": usar como inspiração de estrutura, não copiar o texto para documentos oficiais.
- Conteúdo:

| Nº | Documento | Cláusula da 27001 |
|---|---|---|
| 00/01 | Perfil da empresa (papéis, áreas) | 4 |
| 02 | Escopo do SGSI | 4.3 |
| 03 | Política de segurança da informação | 5.2 |
| 04 | Avaliação de riscos | 6.1.2 / 8.2 |
| 05 | Tratamento de riscos | 6.1.3 / 8.3 |
| 06 | Objetivos de segurança | 6.2 |
| 07 | Evidência de competência | 7.2 |
| 08 | Outros documentos (PCI DSS, fornecedores, retenção) | 7.5 |
| 09 | Planejamento e controle operacional (inclui processo de gestão de mudanças) | 8.1 |
| 10 | Resultados da avaliação de riscos | 8.2 |
| 11 | Decisões de tratamento de riscos | 8.3 |
| 12 | Monitoramento e medição | 9.1 |
| 13 | Auditoria interna | 9.2 |
| 14 | Análise crítica pela direção | 9.3 |
| 15 | Não conformidades e ações corretivas | 10.2 |

- O que aproveitar: a sequência de documentos de um SGSI e o modelo de registro de riscos, de não conformidade e de gestão de mudanças, úteis para documentar um aplicativo como o CifraToken (risco, mudança, teste, aceite).
- Limites: não trata de desenvolvimento seguro em detalhe (nenhum documento cobre 8.25 a 8.29); conteúdo genérico e fictício; nomes com erros de digitação ("Assesment", "Riskk"). Para a AC Defesa, a Política de Segurança v2.0 e a DPC v4.0 prevalecem.

## 2. Claude Skills for Governance, Risk and Compliance

- Endereço: https://github.com/Sushegaad/Claude-Skills-Governance-Risk-and-Compliance (autor Hemant Naik; último commit em 09/10/2026; licença MIT, conferida no arquivo LICENSE).
- O que é: 36 "skills" (pacotes de instruções para o Claude) sobre arcabouços de GRC, cada um com SKILL.md e referências. Os de interesse traziam "Last verified: 2026-10-03".
- Relevantes para o conselho:
  - **ISO 27001**: Anexo A 2022 (93 controles) e 2013, mapeamento entre edições, modelos de análise de lacunas e de Declaração de Aplicabilidade. Confirma 8.28 (codificação segura) e 8.9 (gestão de configuração) como controles novos de 2022.
  - **NIST 800-53**: família SA (aquisição e desenvolvimento): SA-3 ciclo de desenvolvimento, SA-8 princípios de engenharia, SA-10 gestão de configuração do desenvolvedor, SA-11 testes do desenvolvedor, SA-15 processo, padrões e ferramentas, SA-22 componentes sem suporte (liga com o fim do suporte do .NET 8).
  - **CIS Controls**: Controle 16 (segurança de software de aplicação), ex.: 16.9 treinamento de desenvolvedores, 16.12 verificações de segurança no código integradas à CI.
  - **LGPD**: quando o aplicativo tratar dado pessoal.
  - **EU CRA** (Cyber Resilience Act): referência de requisitos de segurança por padrão e gestão de vulnerabilidades em produtos de software; não se aplica à OM, serve só de comparação.
- Como usar: como roteiro e lista de verificação. As skills são escritas por terceiro, e as próprias métricas de avaliação no README divergem entre si; todo número, prazo ou controle citado deve ser conferido na norma original.

## 3. Artigo "C# | Best Practices" (dev.to)

- Endereço: https://dev.to/hbolajraf/c-best-practices-6dp (Hassan Bolajraf, 23/07/2024).
- Quinze recomendações: convenções de nomes; nomes significativos; organização em namespaces; DRY; capturar exceções específicas e só quando souber tratá-las; comentários XML; métodos pequenos; injeção de dependência e interfaces; testes unitários (MSTest, NUnit, xUnit); controle de versão; medir desempenho antes de otimizar e escolher estruturas de dados adequadas; consultas parametrizadas e validação de entrada; SOLID; revisão de código com analisadores; atualização contínua.
- Correção necessária: o artigo recomenda constantes em `MAIÚSCULAS_COM_SUBLINHADO`. A Microsoft recomenda **PascalCase** para constantes (Microsoft Learn, "Identifier names - rules and conventions", consultado em 19/10/2026). Seguir a Microsoft.
- Outro limite: sugere `#region` para organizar código; muitas equipes desaconselham porque esconde classes grandes demais (opinião do especialista 9).
- Valor: lista introdutória boa para alinhar a equipe; não é norma nem cobre segurança além de SQL e validação de entrada. Para segurança, usar OWASP .NET Security Cheat Sheet, ASVS e ISO 27002 8.28.
