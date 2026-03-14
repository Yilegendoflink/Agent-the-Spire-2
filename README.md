# Agent the Spire 2 Mod

这是一个为 Slay the Spire 2 开发的自动化与 AI 智能体 Mod，支持将游戏全部实时状态抽出并通过 WebSocket 推送到外部 Python 后端，并接收指令执行各种复杂决策与游戏动作。
目前确认最新可用游戏版本为v0.98.3

---

## 🚀 核心功能与特色

- **实时对局状态捕获 (WebSocket)**
  捕捉游戏的生命值、金币、当前层数、手牌、抽牌堆、弃牌堆、遗物以及所有敌方实体状态，经 WebSocket（`ws://localhost:8000/ws/game`）输出为标准化 JSON。
  
- **双向游戏互动**
  接受后端 Python/AI 模型传入的 JSON 行动指令，执行出牌、选择地图节点、决定事件选项、进入商店购买/移除卡牌、获取/跳过奖励等完整的流程。
  
- **纯动态游戏文本解析**
  所有卡牌效果、增益与减益 (Buffs/Debuffs) 特性、遗物和药水的描述都会通过读取游戏底层的 `GetFormattedText()` 及实时 `LocString` 数据生成，向 AI 传递**当前回合最详实的加成计算后中文文本**，告别死记硬背机制。

---

## 📥 数据传输结构 (状态汇报格式)

当 Mod 在等候行动/到达新节点时，会传输如下格式的状态 JSON：

```json
{
  "scenario": "combat",          // 场景类型：map(地图), combat(战斗), reward(奖励), event(事件), shop(商店) 等
  "game_state": {
    "hp": 75,
    "max_hp": 80,
    "gold": 120,
    "energy": 3,
    "hand": [
      {
        "id": "Strike",
        "cost": 1,
        "description": "造成 6 点伤害。"
      }
    ],
    "enemies": [
      {
        "id": "JawWorm",
        "hp": 40,
        "max_hp": 44,
        "intent": "Attack",
        "damage": 11,
        "powers": [              // 双端实时Buff增益计算的精准化文本提取
          {
            "id": "Strength",
            "desc": "此生物的攻击造成至少额外 3 点伤害。"
          }
        ]
      }
    ],
    "relics": [...],             
    "potions": [...]             
  }
}
```

---

## 🛠️ 如何下发自动操作指令

外部 Python 发起 Websocket 推送给 Mod。常见指令涵盖：

1. **打出卡牌 (Play Card)**
   ```json
   { "command": "play_card", "card_id": "Strike", "target_index": 0 }
   ```
2. **结束回合 (End Turn)**
   ```json
   { "command": "end_turn" }
   ```
3. **导航/选路线 (Map/Routing)**
   ```json
   { "command": "choose_route", "node_index": 1 }
   ```
4. **选择/跳过奖励 (Rewards)**
   ```json
   { "command": "claim_reward", "index": 0 }
   { "command": "skip_rewards" }
   ```
5. **事件与商店 (Events & Shop)**
   ```json
   { "command": "choose_event_option", "option_index": 0 }
   { "command": "buy_item", "item_id": "Apotheosis" }
   ```

---

## 🎮 快速启动指南

1. **环境准备 & 编译 Mod**
   - 依赖项： [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) 和 Godot 4.5.1 Mono (需在 `local.props` 中配置你的 Steam 路径和 Godot `.exe` 路径)。
   - 在此根目录打开终端运行：
     `dotnet build`
     
2. **运行 Python 服务器**
   在 `python_server` 目录下打开终端：
   ```bash
   pip install -r requirements.txt
   uvicorn server:app --reload
   ```

3. **进入游戏游玩**
   打开 《Slay the Spire 2》，启用了此 Mod 后，在开局选择并获得 `LlmControllerRelic` (或相应遗物)。
   你将在游戏进入节点及战斗时看到后端打出的 `/ws/game [accepted]` 链接提示，代表大语言模型已经掌控该局游戏！


## 该MOD基于lamali292的mod模版仓库制作
模版仓库地址：https://github.com/lamali292/sts2_example_mod