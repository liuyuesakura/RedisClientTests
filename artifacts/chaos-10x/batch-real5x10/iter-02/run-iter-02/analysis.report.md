# Chaos Compare Analysis

- Success rate threshold: 99.5%
- Worst method p95 ratio threshold: 3 x

## Scenario: failover
- Suite csredis: verdict=FAIL; successRate=15.71% ; mixedP95=1.29 ; mixedP99=29.4 ; mixedQps=3480.19 ; worstMethodP95Ratio=1.06
- Suite stackexchange: verdict=FAIL; successRate=92.69% ; mixedP95=692.38 ; mixedP99=1345.87 ; mixedQps=454.67 ; worstMethodP95Ratio=8.48

## Scenario: node-down
- Suite csredis: verdict=FAIL; successRate=0% ; mixedP95=1.21 ; mixedP99=16.23 ; mixedQps=3550.63 ; worstMethodP95Ratio=3.24
- Suite stackexchange: verdict=FAIL; successRate=77.77% ; mixedP95=660.24 ; mixedP99=1116.04 ; mixedQps=500.79 ; worstMethodP95Ratio=10.88

## Scenario: scale-in
- Suite csredis: verdict=FAIL; successRate=0% ; mixedP95=1.29 ; mixedP99=13.11 ; mixedQps=3543.8 ; worstMethodP95Ratio=4.31
- Suite stackexchange: verdict=FAIL; successRate=77.84% ; mixedP95=600.91 ; mixedP99=859.39 ; mixedQps=560.01 ; worstMethodP95Ratio=8.87

