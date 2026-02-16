# APS 排程工作台 UI 设计提示词

本文档包含用于生成或设计 APS L1.5 RCCP 演示界面的 UI 提示词（Prompts）。您可以将这些描述用于设计工具（如 Figma, v0）或作为活字格页面的开发参考。

## 1. 整体风格定义 (Global Style)

**Prompt (English)**:
> "A professional, data-dense enterprise dashboard for manufacturing planning (APS). Clean, modern B2B SaaS interface. Color palette: Neutral grays (#F4F5F7 background), Industrial Blue (#0052CC) for primary actions, semantic colors for status (Green #36B37E for normal, Red #FF5630 for overdue/overload, Amber #FFAB00 for warning). Font: Inter or Roboto, optimized for readability. High contrast for critical data."

**中文描述**:
> 专业的制造业排程管理后台。B2B SaaS 风格，强调数据密度与清晰度。背景使用中性灰，主色调为工业蓝。状态颜色需语义化：绿色代表产能正常，红色代表逾期或超载，琥珀色代表预警。字体清晰，适合长时间操作。

---

## 2. 页面布局：排程工作台 (Scheduling Workbench)

**Layout Prompt**:
> "A full-screen dashboard layout divided into three main sections:
> 1. **Top Header & KPI Bar**: Slim navigation bar with logo 'APS Planner', followed by a KPI strip showing 'Total Orders', 'On-Time Rate', and 'Resource Utilization'.
> 2. **Control Toolbar**: A horizontal bar below the header containing a Date Picker ('Start Date'), a Dropdown ('Scheduling Rule: EDD/WSPT'), and a prominent primary button 'Run Scheduling'.
> 3. **Main Content Area (Split View)**:
>    - **Left Panel (30% width)**: 'Unscheduled Work Orders' list. A vertical list of cards or a compact table showing OrderID, Product, and Due Date.
>    - **Right Panel (70% width)**: 'Capacity & Results'.
>      - **Top Half**: A Stacked Bar Chart showing daily resource load vs capacity limit (red line).
>      - **Bottom Half**: A detailed data grid showing the scheduled results with columns for Date, Resource, Status, and Delay Days."

---

## 3. 组件详细设计 (Component Prompts)

### 3.1 待排产列表 (Left Panel: Unscheduled List)
**Prompt**:
> "A vertical list component titled 'Pending Orders'. Each item is a compact card containing:
> - **Header**: Order ID (e.g., WO-2026-001) in bold.
> - **Body**: 'Product: Widget A', 'Duration: 4h'.
> - **Footer**: A badge showing 'Due: 2026-02-20'.
> - **Style**: White background, subtle shadow, hover effect. Highlight urgent orders with a red left border."

### 3.2 资源负载图表 (Right Panel Top: Resource Load Chart)
**Prompt**:
> "A data visualization component. A stacked bar chart.
> - **X-Axis**: Dates (e.g., Feb 16, Feb 17, Feb 18).
> - **Y-Axis**: Hours (0-40h).
> - **Bars**: Stacked segments representing different work orders assigned to that day.
> - **Reference Line**: A dashed red line across the chart at Y=24h labeled 'Max Capacity'.
> - **Visual Cue**: Any bar segment exceeding the red line should be highlighted in red pattern to indicate overload (though RCCP logic prevents this, visual feedback is good for 'Before/After' comparison)."

### 3.3 排程结果表格 (Right Panel Bottom: Scheduled Grid)
**Prompt**:
> "A high-density data table titled 'Scheduled Results'.
> - **Columns**: Scheduled Date, Resource Group, Order ID, Sequence, Status, Delay Days.
> - **Row Styling**:
>   - Normal rows: White background.
>   - Overdue rows (Delay Days > 0): Light red background (#FFEBE6) with red text.
> - **Cell Highlighting**: The 'Status' column should use badges (Green 'Normal', Red 'Overdue')."

### 3.4 控制栏 (Control Toolbar)
**Prompt**:
> "A functional toolbar with:
> - **Input**: 'Start Date' (Calendar icon).
> - **Select**: 'Rule Strategy' (Options: EDD - Earliest Due Date, SPT - Shortest Processing Time).
> - **Action Button**: 'Execute RCCP'. Large, blue, rounded corners. Icon: Play or Calculator.
> - **Feedback**: A toast notification area for 'Scheduling Complete' messages."

---

## 4. 交互状态 (Interaction States)

### 4.1 逾期预警提示 (Overdue Alert)
**Prompt**:
> "A modal or tooltip that appears when hovering over a red 'Overdue' badge. Text: 'Order WO-005 is delayed by 2 days due to capacity constraints on Feb 16. Suggestion: Check resource availability or change priority.'"

### 4.2 加载状态 (Loading State)
**Prompt**:
> "Skeleton screens for the data grid and chart area while the 'Execute RCCP' command is running. A subtle progress bar at the top of the grid."
