# Chaos Compare Analysis

- Success rate threshold: 99.5%
- Worst method p95 ratio threshold: 3 x

## Scenario: failover
- Suite csredis: verdict=FAIL; successRate=99.78% ; mixedP95=4893.64 ; mixedP99=10375.44 ; mixedQps=90.63 ; worstMethodP95Ratio=96.75
- Suite stackexchange: verdict=FAIL; successRate=85.69% ; mixedP95=642.61 ; mixedP99=1036.12 ; mixedQps=459.92 ; worstMethodP95Ratio=12.43

## Scenario: node-down
- Suite csredis: verdict=FAIL; successRate=77.84% ; mixedP95=3922.78 ; mixedP99=11355.4 ; mixedQps=90.38 ; worstMethodP95Ratio=203.33
- Suite stackexchange: verdict=FAIL; successRate=78.78% ; mixedP95=601.46 ; mixedP99=825.09 ; mixedQps=529.08 ; worstMethodP95Ratio=8.68

## Scenario: scale-in
- Suite csredis: verdict=FAIL; successRate=77.84% ; mixedP95=4271.91 ; mixedP99=10523.55 ; mixedQps=104.73 ; worstMethodP95Ratio=93.85
- Suite stackexchange: verdict=FAIL; successRate=77.84% ; mixedP95=674.84 ; mixedP99=898.41 ; mixedQps=482.67 ; worstMethodP95Ratio=5.95

