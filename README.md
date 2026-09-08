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
- **Background Worker Service**
- **Entity Framework Core**
- **RabbitMQ** (Mensageria com Publisher/Subscriber e DLQ)
- **PostgreSQL** (Persistência relacional isolada por microsserviço)
- **xUnit** (Testes unitários e de integração)

---

## 🚀 Como Executar

### Pré-requisitos
- .NET 9 SDK instalado

### Compilação da Solução

Para restaurar dependências e compilar toda a solução:

```bash
dotnet build OrderFlow.sln
```

### Execução dos Testes

Para rodar todos os testes automatizados da solução:

```bash
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

## 📖 Documentação Detalhada

Para detalhes aprofundados sobre decisões de design, direções de dependência entre camadas, resiliência (Retry e DLQ) e idempotência, consulte o arquivo [ARCHITECTURE.md](file:///e:/projetos/order-flow/ARCHITECTURE.md).
