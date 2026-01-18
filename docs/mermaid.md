```mermaid
flowchart TD
    A[Service Starts] --> B[Query Outbox Table]
    B --> C[Group Messages by Stid]
    C --> D[Process Each Stid Group]

    subgraph Process Stid Group
        D --> E[Separate Messages]
        E --> F[Process Messages with Empty Code]
        F --> G[Simulate Processing]
        G --> H[Wait for Delivery Confirmation]
        H -->|Success| I[Mark Message as Processed]
        H -->|Failure| J[Mark All Messages for Stid as Not Published]
        I --> K[Continue with Non-Empty Code Messages]
        J --> L[Stop Processing This Stid]
        K --> M[Produce to Kafka]
        M --> N[Mark Message as Processed]
    end

    L --> O[Continue with Next Stid]
    N --> O
    J --> O

    subgraph Failure Scenarios
        H -->|Timeout| J
        M -->|Kafka Producer Fails| J
        D -->|Exception Occurs| J
    end

    O --> P[Wait for Next Iteration]
    P --> B