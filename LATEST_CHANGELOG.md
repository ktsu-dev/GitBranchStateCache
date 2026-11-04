## v1.1.2 (patch)

Changes since v1.1.1:

- Match branch patterns on UTF-8 bytes, as git branch --list does [patch] (#119) (matt-edmondson)
- Skip a branch whose name is not UTF-8 instead of failing every ref listing [patch] (#118) (matt-edmondson)
- Refuse a request that names more branch patterns than MaxBranchPatternsPerRequest [patch] (#117) (matt-edmondson)
- Match repository patterns in linear time, and refuse a timed-out match [patch] (#113) (matt-edmondson)

