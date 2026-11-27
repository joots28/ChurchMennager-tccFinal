# Estado de preservação e migração — ChurchManager

**Data da verificação:** 27/11/2026  
**Repositório:** https://github.com/joots28/ChurchMennager-tccFinal  
**Branch verificada:** `fix/api-frontend-integration`

## O que está preservado no GitHub

O repositório contém o código-fonte do frontend e da API, além de arquivos de configuração e documentação. A revisão encontrou alterações recentes no commit:

- `791691743dd2bf46d57cb5fcfcae7ddd157a1327`
- Mensagem: `fix: restore member sync, donation approval, and admin management`

Também foi confirmado que o repositório possui documentação de implantação e configuração do projeto. Isso preserva o código versionado nessa branch; não comprova que existam alterações locais ainda não enviadas.

## O que NÃO é salvo pelo GitHub

O repositório não substitui um backup do PostgreSQL. Não devem ser versionados:

- registros de usuários, igrejas, membros, ofertas, dízimos ou outras movimentações;
- senhas, hashes de credenciais, tokens JWT ou chaves privadas;
- connection strings e arquivos de ambiente com segredos;
- dados pessoais ou financeiros reais.

Arquivos `.env`, `.env.*`, `bin/`, `obj/`, `node_modules/` e `dist/` estão excluídos pelo `.gitignore` verificado.

## Situação do banco antigo

A instância PostgreSQL do Render identificada como `churchmanager-tcc-db` aparece suspensa pelo ciclo de vida do plano Free, com expiração indicada em 21/11/2026. Não foi obtido um dump nem confirmada a recuperação dos registros.

**Não considerar os dados do banco como salvos no GitHub.** A recuperação/exportação precisa ser verificada separadamente no painel do provedor, antes de qualquer exclusão ou substituição.

## Próximas etapas para migrar de conta

1. Confirmar qual banco é realmente usado pela API publicada e qual contém os dados atuais.
2. Se o banco antigo puder ser acessado, gerar um dump seguro fora do repositório e validar sua integridade.
3. Criar o PostgreSQL de destino na nova conta de hospedagem.
4. Aplicar o esquema/migrations compatíveis com o código atual.
5. Importar o dump, quando disponível, e validar contagens e relacionamentos.
6. Configurar secrets exclusivamente no painel do provedor.
7. Publicar API e frontend e testar autenticação, isolamento entre igrejas, membros, administradores e movimentações.

## Atenção à documentação de arquitetura

O README e o plano de deploy descrevem Supabase (`NexEl`) como banco oficial, enquanto a instância Render `churchmanager-tcc-db` também foi identificada como suspensa. Antes de migrar, é obrigatório confirmar no código e nas variáveis do serviço qual banco a API realmente utiliza. Não presumir que os dados estejam no Supabase nem no Render sem essa validação.
