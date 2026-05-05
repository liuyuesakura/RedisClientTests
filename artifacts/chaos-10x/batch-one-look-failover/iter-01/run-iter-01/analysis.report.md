# Chaos Compare Analysis

- Success rate threshold: 99.5%
- Worst method p95 ratio threshold: 3 x

## Scenario: failover
- Suite csredis: verdict=FAIL; class=timeout_dominant; reason=mixed_p95_high_with_failures; successRate=88.9% ; mixedP95=10030.04 ; mixedP99=15122.12 ; mixedQps=14.56 ; worstMethodP95Ratio=82.82
- Suite stackexchange: verdict=FAIL; class=timeout_dominant; reason=mixed_p95_high_with_failures; successRate=95.73% ; mixedP95=18542.44 ; mixedP99=18630.02 ; mixedQps=10.6 ; worstMethodP95Ratio=201.91

