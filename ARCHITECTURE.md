# Arquitetura do OrderFlow

## 1. Princípios e Diretrizes

O **OrderFlow** adota os princípios de **Clean Architecture** (Arquitetura Limpa) e **Domain-Driven Design (DDD) tático**, com foco no desacoplamento entre regras de negócio, infraestrutura de persistência/mensageria e interfaces de entrada.

### 1.1 Regras de Dependência (Clean Architecture)

A direção das dependências no projeto respeita rigorosamente a regra das camadas internas não conhecerem as camadas externas:

```text
       [ Domain ]  <--- (sem dependências de outros projetos da solução)
           ^
           |
      [ Application ]  <--- (depende apenas do Domain e Messaging.Contracts)
           ^
           |
    [ Infrastructure ] <--- (depende de Application, Domain e Messaging.Contracts)
           ^
           |
     [ API / Worker ]  <--- (depende de Infrastructure e Application para composição)
```

1. **Domain (`OrderFlow.*.Domain`)**: Contém entidades, objetos de valor, exceções de domínio e enums. Não possui dependências de bibliotecas de infraestrutura (como Entity Framework Core ou RabbitMQ) nem de outros projetos da solução.
2. **Application (`OrderFlow.*.Application`)**: Contém a orquestração de casos de uso (Commands/Queries), DTOs, validadores e interfaces de repositório e serviços de mensageria.
3. **Infrastructure (`OrderFlow.*.Infrastructure`)**: Implementa as interfaces da camada Application (acesso a banco PostgreSQL via EF Core, publicação/consumo RabbitMQ, etc.).
4. **API / Worker (`OrderFlow.*.Api` e `OrderFlow.*.Worker`)**: Pontos de entrada executáveis responsável pela injeção de dependências (DI), inicialização da aplicação, middlewares e exposição de endpoints ou workers de segundo plano.
5. **BuildingBlocks (`OrderFlow.Messaging.Contracts`)**: Biblioteca compartilhada contendo apenas os contratos de DTOs/Eventos de integração que trafegam entre microsserviços.

---

## 2. Microsserviços e Fronteiras de Contexto (Bounded Contexts)

### 2.1 Orders Service
- **Responsabilidade**: Gerenciar o ciclo de vida dos pedidos (`Pending` -> `Processing` -> `Completed` ou `Cancelled`).
- **Persistência**: Banco isolado `orderflow_orders` no PostgreSQL.
- **Mensageria**: Publica eventos de integração (`OrderCreated`, `OrderStatusChanged`, `OrderCompleted`, `OrderCancelled`) no RabbitMQ após mudanças de estado.

### 2.2 Notifications Service
- **Responsabilidade**: Consumir eventos publicados pelo *Orders Service*, converter em notificações auditáveis e garantir idempotência.
- **Persistência**: Banco isolado `orderflow_notifications` no PostgreSQL (tabelas `Notifications` e `ProcessedMessages`).
- **Mensageria**: Consome da fila `orderflow.notifications` com suporte a políticas de retry e Dead Letter Queue (DLQ).

---

## 3. Comunicação Assíncrona e Eventos

### 3.1 Domain Events vs. Integration Events

O OrderFlow estabelece uma separação clara entre **Domain Events** (eventos internos ao domínio) e **Integration Events** (eventos de integração entre microsserviços):

| Característica | Domain Event (`OrderFlow.*.Domain.Events`) | Integration Event (`OrderFlow.Messaging.Contracts.Events`) |
| :--- | :--- | :--- |
| **Escopo** | Intra-processo, restrito ao mesmo Bounded Context (ex.: dentro do microsserviço de Orders). | Inter-processo / Distribuído, compartilhado entre múltiplos Bounded Contexts (Orders -> Notifications). |
| **Acoplamento** | Acoplado aos tipos e entidades do Domínio (`OrderStatus`, etc.). Não deve sair do domínio. | 100% desacoplado de entidades de domínio. Utiliza apenas tipos primitivos/escalares e DTOs imutáveis. |
| **Transporte** | Disparado e tratado em memória (síncrono ou assíncrono local) dentro da mesma transação/Unit of Work. | Serializado (JSON) e publicado assincronamente através de um message broker (RabbitMQ). |
| **Finalidade** | Notificar outras partes do mesmo agregado/domínio sobre regras de negócio que mudaram de estado. | Notificar outros sistemas/microsserviços sobre fatos consumados de interesse corporativo. |
| **Versionamento** | Raro/desnecessário (refatorado junto com o código da aplicação). | Obrigatório e explícito (`version: 1`), pois múltiplos consumidores externos dependem do contrato estável. |
| **Rastreabilidade** | Contextual à execução da thread/tarefa corrente. | Exige envelope com metadados distribuídos (`EventId`, `CorrelationId`, `OccurredAt`). |

### 3.2 Estrutura do Envelope de Integração (`EventEnvelope<T>`)

Para garantir interoperabilidade e rastreabilidade entre microsserviços sem compartilhar modelos de dados complexos, todas as mensagens transitam encapsuladas em um envelope padronizado:

```json
{
  "eventId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "eventType": "OrderCreated",
  "occurredAt": "2026-08-18T12:00:00Z",
  "version": 1,
  "correlationId": "req-987654321",
  "data": {
    "orderId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "customerName": "John Doe",
    "customerEmail": "john.doe@example.com",
    "totalAmount": 150.00,
    "status": "Pending",
    "createdAt": "2026-08-18T12:00:00Z"
  }
}
```

- **`EventId`**: Identificador único global do evento para controle de deduplicação e idempotência no consumidor.
- **`EventType`**: String descritiva da intenção/evento para roteamento e serialização polimórfica.
- **`OccurredAt`**: Timestamp UTC do momento em que o evento ocorreu.
- **`Version`**: Versão do contrato de dados (versão inicial: `1`).
- **`CorrelationId`**: Identificador de rastreamento ponta a ponta (Tracing distribuído).
- **`Data`**: Payload específico do evento com dados enxutos e estritamente necessários.

### 3.3 Contratos de Eventos de Integração Disponíveis

- **`OrderCreatedIntegrationEvent`**: `(Guid OrderId, string CustomerName, string CustomerEmail, decimal TotalAmount, string Status, DateTimeOffset CreatedAt)`
- **`OrderStatusChangedIntegrationEvent`**: `(Guid OrderId, string PreviousStatus, string NewStatus, DateTimeOffset ChangedAt)`
- **`OrderCompletedIntegrationEvent`**: `(Guid OrderId, DateTimeOffset CompletedAt)`
- **`OrderCancelledIntegrationEvent`**: `(Guid OrderId, string PreviousStatus, DateTimeOffset CancelledAt, string? Reason = null)`

### 3.4 Topologia RabbitMQ no Orders Service

O microsserviço de Orders publica eventos através da implementação `RabbitMqEventPublisher` (na camada `OrderFlow.Orders.Infrastructure`), atendendo à abstração `IEventPublisher` da camada Application.

- **Exchange**: `orderflow.orders`
  - **Tipo**: `topic`
  - **Durabilidade**: `durable: true`, `autoDelete: false`
- **Routing Keys**:
  - `order.created`: Publicado imediatamente após a criação do pedido ser persistida com sucesso.
  - `order.status.changed`: Publicado a cada transição de status do pedido.
  - `order.completed`: Publicado quando o pedido atinge o estado final `Completed`.
  - `order.cancelled`: Publicado quando o pedido é cancelado (`Cancelled`).
- **Propriedades da Mensagem AMQP**:
  - `ContentType`: `application/json`
  - `ContentEncoding`: `utf-8`
  - `DeliveryMode`: `Persistent` (2)
  - `MessageId`: `EventEnvelope.EventId` (UUID)
  - `CorrelationId`: `EventEnvelope.CorrelationId`
  - `Type`: Nome do evento (`OrderCreated`, etc.)
  - `Timestamp`: Unix Epoch do `OccurredAt`

---

## 4. Padrões Distribuídos e Limitações Técnicas

### 4.1 Limitação Técnica: Ausência de Transactional Outbox (Dual-Write Problem)

> [!WARNING]
> **Limitação Técnica Atual**: O microsserviço de Orders publica mensagens no RabbitMQ de forma síncrona diretamente nos casos de uso após a persistência no banco de dados (`SaveChangesAsync`).
> 
> **Impacto Arquitetural**:
> 1. **Dual-Write Problem**: Como a escrita no PostgreSQL e a publicação no RabbitMQ não compartilham uma transação atômica distribuída (2PC / XA), há um ponto de falha onde o pedido pode ser gravado com sucesso no banco, mas a publicação no broker falhar (ex.: indisponibilidade transitória de rede, reinício do broker).
> 2. **Semântica de Entrega**: Atualmente opera em regime de *melhor esforço* (*at-most-once* para publicação), o que pode acarretar em mensagens perdidas em caso de indisponibilidade no momento do disparo.
> 
> **Evolução Arquitetural Planejada**:
> Em etapas subsequentes de maturidade da plataforma, essa limitação será mitigada com a implementação do **Transactional Outbox Pattern**:
> - O caso de uso gravará o agregado `Order` e o registro do evento na tabela `OutboxMessages` dentro da **mesma transação relacional local** do PostgreSQL.
> - Um processo em background (*BackgroundService* / Worker ou CDC com Debezium) fará o pooling/polling e a publicação garantida no RabbitMQ com confirmações (*publisher confirms*), assegurando semântica *at-least-once* ponta a ponta.

### 4.2 Idempotência
- O consumidor consulta e registra o `eventId` na tabela `ProcessedMessages` antes de processar, evitando duplicidade de efeitos colaterais em reentregas.

### 4.3 Tratamento de Erros e DLQ
- Falhas transitórias no consumidor são retentadas em até N vezes. Persistindo a falha, a mensagem é encaminhada para `orderflow.notifications.dlq`.

