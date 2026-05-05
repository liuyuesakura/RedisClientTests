# Chaos Compare Analysis

- Success rate threshold: 99.5%
- Worst method p95 ratio threshold: 3 x

## Scenario: failover
- Suite csredis: verdict=FAIL; successRate=15.78% ; mixedP95=1.01 ; mixedP99=18.29 ; mixedQps=3507.43 ; worstMethodP95Ratio=1.02
- Suite stackexchange: verdict=FAIL; successRate=96.59% ; mixedP95=713.85 ; mixedP99=1151.75 ; mixedQps=446.88 ; worstMethodP95Ratio=11.98

## Scenario: node-down
- Suite csredis: verdict=FAIL; successRate=0% ; mixedP95=1.97 ; mixedP99=26.51 ; mixedQps=3490.42 ; worstMethodP95Ratio=7.16
- Suite stackexchange: verdict=FAIL; successRate=77.48% ; mixedP95=541.46 ; mixedP99=761.65 ; mixedQps=594.78 ; worstMethodP95Ratio=9.36

## Scenario: scale-in
- Suite csredis: verdict=FAIL; successRate=0% ; mixedP95=1.06 ; mixedP99=12.16 ; mixedQps=3556.13 ; worstMethodP95Ratio=2
- Suite stackexchange: verdict=FAIL; successRate=77.84% ; mixedP95=513.83 ; mixedP99=749.4 ; mixedQps=586.97 ; worstMethodP95Ratio=9.78

