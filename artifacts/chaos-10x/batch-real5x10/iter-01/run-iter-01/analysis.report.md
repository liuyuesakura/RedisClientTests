# Chaos Compare Analysis

- Success rate threshold: 99.5%
- Worst method p95 ratio threshold: 3 x

## Scenario: failover
- Suite csredis: verdict=FAIL; successRate=15.8% ; mixedP95=1.22 ; mixedP99=14.78 ; mixedQps=3516.81 ; worstMethodP95Ratio=2.67
- Suite stackexchange: verdict=FAIL; successRate=96.5% ; mixedP95=728.01 ; mixedP99=1133.55 ; mixedQps=445.9 ; worstMethodP95Ratio=9.81

## Scenario: node-down
- Suite csredis: verdict=FAIL; successRate=0% ; mixedP95=1.9 ; mixedP99=14.36 ; mixedQps=3514.85 ; worstMethodP95Ratio=8.38
- Suite stackexchange: verdict=FAIL; successRate=77.79% ; mixedP95=528.33 ; mixedP99=729.37 ; mixedQps=583.54 ; worstMethodP95Ratio=9.33

## Scenario: scale-in
- Suite csredis: verdict=FAIL; successRate=0% ; mixedP95=0.75 ; mixedP99=8.31 ; mixedQps=3633.2 ; worstMethodP95Ratio=2.58
- Suite stackexchange: verdict=FAIL; successRate=77.84% ; mixedP95=604.23 ; mixedP99=975.27 ; mixedQps=526.68 ; worstMethodP95Ratio=9.77

