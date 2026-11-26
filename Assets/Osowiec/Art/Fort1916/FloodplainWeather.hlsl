#ifndef OSOWIEC_FLOODPLAIN_WEATHER_INCLUDED
#define OSOWIEC_FLOODPLAIN_WEATHER_INCLUDED

// A moving gust front crosses river and reed beds in the same direction.
// All phases use world metres so separate clusters meet continuously.
float2 FloodplainGust(float2 p, float time)
{
    float front = dot(p, float2(.25, -.12)) - time * .94;
    float strength = .40 + .30 * sin(front + .53 * sin(p.y * .19)) +
                     .16 * sin(front * 1.69 + p.x * .10 - time * .18);
    float crosswind = .15 * sin(dot(p, float2(.53,.74)) - time * 1.44);
    return float2(strength + crosswind * .43, strength * -.43 + crosswind);
}

float3 BendFloodplainLeaf(float3 p, float tip)
{
    float2 wind = FloodplainGust(p.xz, _Time.y);
    float flutter = sin(dot(p.xz, float2(3.8, 1.67)) - _Time.y * 3.3);
    float bend = tip * tip;
    p.xz += (.105 * wind + float2(.009, .007) * flutter) * bend;
    p.y -= dot(wind,wind) * .018 * bend;
    return p;
}

#endif
