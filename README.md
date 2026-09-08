# OrderFlow

**OrderFlow** é um projeto de demonstração prática de arquitetura de microsserviços distribuídos construído no ecossistema .NET 9. Ele simula o processamento e fluxo de vida de pedidos com mensageria assíncrona, desacoplamento de serviços e aderência aos princípios de **Clean Architecture**.

---

## 🏗️ Visão Geral da Arquitetura

O sistema é dividido em dois microsserviços principais e uma biblioteca compartilhada de contratos:

1. **Orders Service**: Responsável pelo gerenciamento do ciclo de vida de pedidos (Criação, Alteração de Status, Cancelamento).
2. **Notifications Service**: Worker responsável por consumir eventos de domínio via RabbitMQ e registrar notificações persistidas.
3. **BuildingBlocks (Messaging.Contracts)**: Contratos e envelopes de eventos distribuídos compartilhados entre microsserviços.

---

## 📁 Estrutura da Solução

```text
OrderFlow/
├── src/
│   ├── BuildingBlocks/
│   │   └── OrderFlow.Messaging.Contracts/         # Contratos de eventos desacoplados
│   └── Services/
│       ├── Orders/                                # Microsserviço de Pedidos
│       │   ├── OrderFlow.Orders.Domain/           # Entidades, Value Objects e Regras de Negócio
│       │   ├── OrderFlow.Orders.Application/      # Casos de Uso, DTOs e Interfaces
│       │   ├── OrderFlow.Orders.Infrastructure/   # EF Core, PostgreSQL e RabbitMQ Publisher
│       │   └── OrderFlow.Orders.Api/              # REST API (ASP.NET Core)
│       └── Notifications/                         # Microsserviço de Notificações
│           ├── OrderFlow.Notifications.Domain/
│           ├── OrderFlow.Notifications.Application/
│           ├── OrderFlow.Notifications.Infrastructure/
│           └── OrderFlow.Notifications.Worker/   # Worker Service consumidor do RabbitMQ
└── tests/
    ├── Orders/
    │   ├── OrderFlow.Orders.Domain.Tests/        # Testes unitários do domínio Orders
    │   ├── OrderFlow.Orders.Application.Tests/   # Testes unitários de aplicação
    │   └── OrderFlow.Orders.IntegrationTests/    # Testes de integração
    └── Notifications/
        └── OrderFlow.Notifications.Tests/        # Testes de notificações
```

---

## 🛠️ Tecnologias Utilizadas

- **.NET 9 SDK** (C# 13)
- **ASP.NET Core Web API**
- **Docker & Docker Compose** (Containerização e orquestração local)
- **Background Worker Service**
- **Entity Framework Core**
- **RabbitMQ** (Mensageria com Publisher/Subscriber e DLQ)
- **PostgreSQL** (Persistência relacional isolada por microsserviço)
- **xUnit** (Testes unitários e de integração)

---

## 🚀 Como Executar

### Pré-requisitos
- **Docker** e **Docker Compose** instalados (ou .NET 9 SDK para execução local direta).

---

### 🐳 Execução Completa com Docker Compose (Recomendado)

Para subir todo o ecossistema (Orders API, Notifications Worker, 2 bancos PostgreSQL isolados e RabbitMQ Management) de forma reproduzível:

1. **Configurar variáveis de ambiente**:
   ```bash
   cp .env.example .env
   ```

2. **Iniciar todos os serviços com build**:
   ```bash
   docker compose up --build -d
   ```

3. **Portas e Serviços Disponíveis**:
   - 🌐 **Orders API (Swagger / OpenAPI)**: [http://localhost:5000/swagger](http://localhost:5000/swagger)
   - 🐰 **RabbitMQ Management UI**: [http://localhost:15672](http://localhost:15672) (Credenciais: `guest` / `guest`)
   - 🐘 **Orders Database (PostgreSQL)**: `localhost:5433` (Database: `orderflow_orders`, User: `postgres`, Password: `postgres`)
   - 🐘 **Notifications Database (PostgreSQL)**: `localhost:5434` (Database: `orderflow_notifications`, User: `postgres`, Password: `postgres`)

4. **Acompanhar os logs estruturados**:
   ```bash
   docker compose logs -f
   ```

5. **Parar e remover os containers e volumes**:
   ```bash
   docker compose down -v
   ```

---

### 💻 Execução Local Direta (.NET CLI)

Caso deseje compilar e rodar localmente sem containers:

```bash
# Restaurar dependências e compilar a solução
dotnet build OrderFlow.sln

# Executar todos os testes automatizados
dotnet test OrderFlow.sln
```

---

## 📬 Mensageria, Resiliência e Dead Letter Queue (DLQ)

O serviço de notificações (`OrderFlow.Notifications.Worker`) implementa tratamento explícito de falhas com proteção contra loops infinitos e perda de mensagens:

- **Política de Retry**: Até 3 tentativas com backoff exponencial para falhas transitórias (banco indisponível, timeouts de rede, concorrência).
- **Tratamento de Poison Messages**: Mensagens definitivamente inválidas (JSON corrompido, envelope sem payload, tipo de evento desconhecido) são rejeitadas imediatamente (`requeue: false`), sendo enviadas diretamente para a DLQ sem desperdiçar retries.
- **Dead Letter Topology**:
  - **Exchange DLX**: `orderflow.notifications.dlx` (Direct)
  - **Fila DLQ**: `orderflow.notifications.dlq` (Durable)
  - **Fila Principal**: `orderflow.notifications` com `x-dead-letter-exchange: orderflow.notifications.dlx` e `x-dead-letter-routing-key: orderflow.notifications.dlq`.
- **Confirmação Estrita**: Mensagens só recebem `BasicAck` após processamento com sucesso (ou confirmação de duplicata idempotente). Em falhas terminais, o `BasicNack(requeue: false)` garante o roteamento automático para a DLQ pelo RabbitMQ.

### 🔍 Como Visualizar e Inspecionar a DLQ no RabbitMQ Management

1. **Acesse o painel web do RabbitMQ**:
   Abra no navegador: `http://localhost:15672` (Usuário padrão: `guest` / Senha: `guest`).

2. **Navegue até as Filas**:
   Clique no menu **Queues and Streams** no topo do painel.

3. **Localize a fila da DLQ**:
   Na lista de filas, clique em `orderflow.notifications.dlq`.

4. **Inspecione as mensagens retidas**:
   - Role até a seção **Get messages**.
   - Defina **Messages**: `1` (ou a quantidade desejada).
   - Defina **Requeue**: `Yes` (para apenas inspecionar sem remover) ou `No` (para consumir e remover da DLQ).
   - Clique no botão **Get Message(s)**.

5. **O que analisar na mensagem na DLQ**:
   - **Payload**: O conteúdo JSON original do evento com os dados intactos (`EventId`, `Data`, `CorrelationId`).
   - **Headers (`x-death`)**: Array contendo metadados injetados nativamente pelo RabbitMQ:
     - `reason`: Motivo da rejeição (`rejected`).
     - `queue`: Fila de origem onde ocorreu a falha (`orderflow.notifications`).
     - `count`: Quantidade de vezes que a mensagem sofreu dead-lettering.
     - `time`: Timestamp exato da ocorrência do erro.
   - **Properties**: `correlation_id`, `message_id`, `type` e `content_type`.

---

## 📊 Observabilidade e Logs Estruturados (Serilog)

O OrderFlow utiliza **Serilog** com suporte nativo a rastreabilidade ponta a ponta via `CorrelationId` e enriquecimento de propriedades em ambos os microsserviços:

- **Propriedades Estruturadas**: `ServiceName`, `CorrelationId`, `OrderId`, `EventId`, `EventType`, `DeliveryTag`, `StatusCode`, etc.
- **Saída em Desenvolvimento**: Formato textual com destaque e contexto:
  ```text
  [10:15:30 INF] [Orders.Api] [9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d] Order f47ac10b-58cc-4372-a567-0e02b2c3d479 successfully created with status 'Pending'.
  ```
- **Saída para Containers (JSON)**: Ativada em produção ou configurando `"Serilog:UseJsonConsole": true`, emitindo formato compacto JSON (`clef`) pronto para ingestão em ElasticSearch, Grafana Loki, Fluentbit ou AWS CloudWatch.
- **Segurança de Dados**: Credenciais de banco/RabbitMQ e dados sensíveis de usuários são mascarados/omitidos.

---

## 🧪 Estratégia e Execução de Testes

A suíte de testes do OrderFlow foi desenhada para garantir alta fidelidade aos fluxos de negócio críticos e contratos de integração sem buscar coberturas artificiais de 100%:

### 🏛️ Estrutura da Pirâmide de Testes

1. **Testes de Domínio (Unitários)**:
   - Validação de regras e invariantes de negócio na entidade `Order` (`OrderFlow.Orders.Domain.Tests`).
   - Cobertura exaustiva de transições válidas e inválidas da máquina de estados (`Pending -> Processing -> Completed / Cancelled`).
   - Geração correta e isolada de eventos de domínio (`OrderCreatedDomainEvent`, `OrderStatusChangedDomainEvent`, etc.).
   - Sanitização de entradas e validação de invariantes sem dependência de I/O externo.

2. **Testes de Aplicação (Unitários com Mocks)**:
   - Cobertura dos casos de uso de criação, consulta por ID, listagem, alteração de status e cancelamento (`OrderFlow.Orders.Application.Tests`).
   - Validação de publicação de contratos de mensageria envelopados (`EventEnvelope<T>`) com propagação correta de `CorrelationId`.
   - Idempotência nos casos de uso de notificações (`OrderFlow.Notifications.Tests.Application`), validando descarte seguro de mensagens duplicadas sem reprocessamento ou efeitos colaterais.

3. **Testes de Integração HTTP (WebApplicationFactory)**:
   - Teste de comportamento dos endpoints REST da Orders API (`OrderFlow.Orders.IntegrationTests.Controllers`).
   - Respostas esperadas e mapeamentos de status: `201 Created` (com header `Location`), `400 Bad Request` (com `ProblemDetails` e mensagens de validação detalhadas) e `404 Not Found`.
   - Propagação e injeção do header HTTP `X-Correlation-ID`.

4. **Testes de Persistência Real (PostgreSQL & Testcontainers)**:
   - Testes de integração de banco de dados executando contra instâncias reais e efêmeras de PostgreSQL via **Testcontainers** (`Testcontainers.PostgreSql`).
   - Execução das migrações reais do Entity Framework Core (`Database.MigrateAsync()`).
   - Validação de tipos nativos do PostgreSQL (`numeric(18,2)`, `varchar`, `timestamp with time zone`) e restrições de chave primária/unicidade para tabelas como `ProcessedMessages` e `Notifications`.

---

### ▶️ Como Executar os Testes

#### 1. Executar Toda a Suíte (Unitários + Integração):
```bash
dotnet test
```

#### 2. Executar Projetos Específicos:
```bash
# Testes unitários de Domínio (Orders)
dotnet test tests/Orders/OrderFlow.Orders.Domain.Tests/

# Testes unitários de Aplicação (Orders)
dotnet test tests/Orders/OrderFlow.Orders.Application.Tests/

# Testes de Notificações (Aplicação, Domínio, Idempotência e Persistência)
dotnet test tests/Notifications/OrderFlow.Notifications.Tests/

# Testes de Integração de Orders (HTTP Controllers + Repositórios PostgreSQL)
dotnet test tests/Orders/OrderFlow.Orders.IntegrationTests/
```

---

## 📖 Documentação Detalhada

Para detalhes aprofundados sobre decisões de design, direções de dependência entre camadas, resiliência (Retry e DLQ), idempotência e exemplos completos de logs JSON, consulte o arquivo [ARCHITECTURE.md](file:///e:/projetos/order-flow/ARCHITECTURE.md).


