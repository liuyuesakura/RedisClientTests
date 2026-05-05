# Chaos Compare Analysis

- Success rate threshold: 99.5%
- Worst method p95 ratio threshold: 3 x

## Scenario: failover
- Suite csredis: verdict=FAIL; class=mixed_or_unknown; reason=no_single_dominant_pattern; successRate=100% ; mixedP95=10611.3 ; mixedP99=20732.28 ; mixedQps=87.36 ; worstMethodP95Ratio=200.44
- Suite stackexchange: verdict=FAIL; class=timeout_dominant; reason=mixed_p95_high_with_failures; successRate=99.99% ; mixedP95=2654.08 ; mixedP99=21899.6 ; mixedQps=199.28 ; worstMethodP95Ratio=23.81

## Scenario: node-down
- Suite csredis: verdict=FAIL; class=timeout_dominant; reason=mixed_p95_high_with_failures; successRate=99.6% ; mixedP95=12383.74 ; mixedP99=24848.78 ; mixedQps=76.69 ; worstMethodP95Ratio=128.95
- Suite stackexchange: verdict=FAIL; class=mixed_or_unknown; reason=no_single_dominant_pattern; successRate=100% ; mixedP95=1653.85 ; mixedP99=9752.01 ; mixedQps=227.05 ; worstMethodP95Ratio=22.75

## Scenario: scale-in
- Suite csredis: verdict=FAIL; class=timeout_dominant; reason=mixed_p95_high_with_failures; successRate=99.03% ; mixedP95=10533.29 ; mixedP99=27517.21 ; mixedQps=85.32 ; worstMethodP95Ratio=135.53
- Suite stackexchange: verdict=FAIL; class=mixed_or_unknown; reason=no_single_dominant_pattern; successRate=99.9% ; mixedP95=1692.46 ; mixedP99=10303.13 ; mixedQps=233.43 ; worstMethodP95Ratio=21.65

